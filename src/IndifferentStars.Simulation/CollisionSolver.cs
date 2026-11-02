using System;
using IndifferentStars.Shared;

namespace IndifferentStars.Simulation;

/// <summary>Continuous sphere contacts; all lengths are in metres.</summary>
public static class CollisionSolver
{
    public const double ShipRadius = 10;

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

    public static void Stop(ShipState ship, Vec3d start, double time)
    {
        ship.Position = start + (ship.Position - start) * Math.Max(0, time - 1e-6);
        ship.Velocity = Vec3d.Zero;
        ship.Throttle = 0;
        ship.Boosting = false;
        ship.Cruising = false;
        ship.CruiseInhibited = true;
        ship.CruiseBraking = false;
        ship.ApproachTargetId = 0;
        ship.ApproachFollowing = false;
        ship.DockingGranted = false;
    }
}
