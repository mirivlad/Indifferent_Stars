using System;
using IndifferentStars.Shared;

namespace IndifferentStars.Simulation;

/// <summary>Analytic sphere contact for celestial surfaces; all lengths are in metres.</summary>
public static class CollisionSolver
{
    public static double? ContactTime(Vec3d start, Vec3d end, Vec3d center, double radius)
    {
        var offset = start - center;
        var motion = end - start;
        var c = offset.LengthSquared - radius * radius;
        if (c <= 0)
            return Vec3d.Dot(offset, motion) < 0 ? 0 : null;
        var a = motion.LengthSquared;
        if (a < 1e-16) return null;
        var b = Vec3d.Dot(offset, motion);
        if (b >= 0) return null;
        var discriminant = b * b - a * c;
        if (discriminant < 0) return null;
        var t = c / (-b + Math.Sqrt(discriminant));
        return t <= 1 ? Math.Max(0, t) : null;
    }

    public const double Restitution = .18;
    public const double Friction = .35;

    public static (double NormalSpeed, double TangentialSpeed) Resolve(ShipState a, ShipState? b,
        Vec3d point, Vec3d normal, Vec3d surfaceVelocity, Vec3d? angularA = null, Vec3d? angularB = null)
    {
        normal=normal.Normalized();
        var ra=point-a.Position;var rb=b is null?Vec3d.Zero:point-b.Position;
        var ma=a.Docked?0:1/a.MassKg;var mb=b is null||b.Docked?0:1/b.MassKg;
        var ia=InverseInertia(a);var ib=b is null?0:InverseInertia(b);
        var va=a.Velocity+Vec3d.Cross(angularA??a.AngularVelocity,ra);
        var vb=b is null?surfaceVelocity:b.Velocity+Vec3d.Cross(angularB??b.AngularVelocity,rb);
        var relative=va-vb;
        var closing=Vec3d.Dot(relative,normal);
        var tangent=relative-normal*closing;
        var tangentSpeed=tangent.Length;
        if (closing>=-1e-8||ma+mb<=0) return (0,tangentSpeed);
        // Bound spin by increasing effective rotational inertia for violent hits.
        // Recompute the linear impulse with that inertia so the contact still separates.
        var upperImpulse=-(1+Restitution)*closing/(ma+mb)*Math.Sqrt(1+Friction*Friction);
        ia=Math.Min(ia,Math.Max(0,2-a.AngularVelocity.Length)/Math.Max(1e-12,ra.Length*upperImpulse));
        if(b is not null)ib=Math.Min(ib,Math.Max(0,2-b.AngularVelocity.Length)/Math.Max(1e-12,rb.Length*upperImpulse));
        var rnA=Vec3d.Cross(ra,normal);var rnB=Vec3d.Cross(rb,normal);
        var denominator=ma+mb+rnA.LengthSquared*ia+rnB.LengthSquared*ib;
        var impulse=-(1+(closing<-.5?Restitution:0))*closing/denominator;
        var total=normal*impulse;
        if (tangentSpeed>1e-7)
        {
            var direction=tangent/tangentSpeed;
            var rtA=Vec3d.Cross(ra,direction);var rtB=Vec3d.Cross(rb,direction);
            var friction=Math.Min(Friction*impulse,tangentSpeed/(ma+mb+rtA.LengthSquared*ia+rtB.LengthSquared*ib));
            total-=direction*friction;
        }
        Apply(a,total,ra,ma,ia);
        if(b is not null) Apply(b,-total,rb,mb,ib);
        return (-closing,tangentSpeed);
    }

    private static double InverseInertia(ShipState ship) => ship.Docked?0:
        1/(ship.MassKg*Math.Pow(IndifferentStars.Shared.Collision.SurfaceMesh.Ship.Radius*(ship.IsPlayer?1:IndifferentStars.Shared.Collision.ModelPose.NpcScale),2)*.4);

    private static void Apply(ShipState ship,Vec3d impulse,Vec3d arm,double inverseMass,double inverseInertia)
    {
        if(inverseMass==0)return;
        ship.Velocity+=impulse*inverseMass;
        ship.AngularVelocity+=Vec3d.Cross(arm,impulse)*inverseInertia;
        if(ship.AngularVelocity.Length>2)ship.AngularVelocity=ship.AngularVelocity.Normalized()*2;
    }

    public static void CancelNavigation(ShipState ship)
    {
        ship.Throttle=0;ship.Boosting=false;ship.Cruising=false;ship.CruiseInhibited=true;
        ship.CruiseBraking=false;ship.ApproachTargetId=0;ship.RouteActive=false;ship.ApproachFollowing=false;ship.DockingGranted=false;
    }
}
