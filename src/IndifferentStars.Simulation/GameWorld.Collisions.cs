using System;
using System.Collections.Generic;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed partial class GameWorld
{
    private void ResolveCollisions(ShipState[] ships,Dictionary<uint,(Vec3d Position,Quaternion Orientation)> starts,HashSet<uint> warping,double dt)
    {
        void AtContact(ShipState ship,double time)
        {
            if(ship.Docked)return;
            var before=starts[ship.Id];
            ship.Position=Vec3d.Lerp(before.Position,ship.Position,Math.Max(0,time-1e-7));
            ship.Orientation=Quaternion.Slerp(before.Orientation,ship.Orientation,(float)time);
            CollisionSolver.CancelNavigation(ship);
        }
        void Record(ShipState ship,SurfaceHit hit,double speed,double sliding)
        {
            if(ship.Docked||speed<.2)return;
            // Sustained rubbing refreshes at most five times a second; a new hard hit is immediate.
            if(TimeSeconds-ship.ImpactAt<.2 && speed<5)return;
            ship.ImpactAt=TimeSeconds;
            ship.Impact=new ImpactState(++ship.ImpactSequence,hit.Point,hit.Normal,(float)speed,(float)sliding);
        }
        foreach(var ship in ships)
        {
            if(ship.Docked||warping.Contains(ship.Id))continue;
            var before=starts[ship.Id];
            var from=WorldSurfaces.Ship(ship,before.Position,before.Orientation);
            var to=WorldSurfaces.Ship(ship,ship.Position,ship.Orientation);
            SurfaceHit? first=null;StaticBody? contactBody=null;
            void Hit(SurfaceHit? candidate,StaticBody? body=null)
            {
                if(candidate is not { } hit||first is { } known&&hit.Time>=known.Time)return;
                first=hit;contactBody=body;
            }
            foreach(var body in _collisionBodies)
                Hit(WorldSurfaces.ContactHit(from,to,BodySurface(body,TimeSeconds),BodySurface(body,TimeSeconds+dt)),body);
            Hit(WorldSurfaces.SphereHit(from,to,Catalog.CelestialOffset(Catalog.Planet),Catalog.Planet.Radius));
            Hit(WorldSurfaces.SphereHit(from,to,Catalog.CelestialOffset(Catalog.Star),Catalog.Star.Radius));
            if(first is not { } impact)continue;
            var angular=SurfaceContact.AngularMotion(before.Orientation,ship.Orientation)/dt;
            AtContact(ship,impact.Time);
            var surface=contactBody?.Kind==BodyKind.Station
                ? Vec3d.Cross(new Vec3d(0,ModelPose.StationSpin,0),impact.Point-contactBody.Position):Vec3d.Zero;
            var response=CollisionSolver.Resolve(ship,null,impact.Point,impact.Normal,surface,angular);
            ship.Position+=impact.Normal*.02;
            if(contactBody?.Kind==BodyKind.Station)
            {
                var contactRotation=ModelPose.StationRotation(contactBody,TimeSeconds+dt*impact.Time);
                var endRotation=ModelPose.StationRotation(contactBody,TimeSeconds+dt);
                ship.Position=contactBody.Position+SurfaceMesh.Rotate(ship.Position-contactBody.Position,endRotation*Quaternion.Conjugate(contactRotation));
            }
            Record(ship,impact,response.NormalSpeed,response.TangentialSpeed);
        }
        for(var i=0;i<ships.Length;i++)for(var j=i+1;j<ships.Length;j++)
        {
            var a=ships[i];var b=ships[j];
            if(a.Docked&&b.Docked||warping.Contains(a.Id)||warping.Contains(b.Id))continue;
            var sa=starts[a.Id];var sb=starts[b.Id];
            var impact=WorldSurfaces.ContactHit(WorldSurfaces.Ship(a,sa.Position,sa.Orientation),WorldSurfaces.Ship(a,a.Position,a.Orientation),
                WorldSurfaces.Ship(b,sb.Position,sb.Orientation),WorldSurfaces.Ship(b,b.Position,b.Orientation));
            if(impact is not { } hit)continue;
            var wa=SurfaceContact.AngularMotion(sa.Orientation,a.Orientation)/dt;
            var wb=SurfaceContact.AngularMotion(sb.Orientation,b.Orientation)/dt;
            AtContact(a,hit.Time);AtContact(b,hit.Time);
            var response=CollisionSolver.Resolve(a,b,hit.Point,hit.Normal,Vec3d.Zero,wa,wb);
            var inverseA=a.Docked?0:1/a.MassKg;var inverseB=b.Docked?0:1/b.MassKg;
            var sum=inverseA+inverseB;
            a.Position+=hit.Normal*(.04*inverseA/sum);b.Position-=hit.Normal*(.04*inverseB/sum);
            Record(a,hit,response.NormalSpeed,response.TangentialSpeed);
            Record(b,hit with {Normal=-hit.Normal},response.NormalSpeed,response.TangentialSpeed);
        }
    }
}
