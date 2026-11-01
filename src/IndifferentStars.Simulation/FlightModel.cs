using System;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

/// <summary>
/// Arcade flight model with inertia and speed limits. The model is shared by every
/// representation; only input mapping differs on the client.
/// </summary>
public static class FlightModel
{
    public const double Acceleration = 150.0;
    public const double ReverseFactor = 0.45;
    public const double VerticalAcceleration = 85.0;
    public const double LinearDrag = 0.35;
    public const double BrakeDrag = 5.0;
    public const double YawRate = 1.15;
    public const double PitchRate = 0.85;
    public const double MaxPitch = 1.45;
    public const double ThrottleResponse = 2.4;

    public static (Vec3d Forward, Vec3d Right, Vec3d Up) Basis(double yaw, double pitch)
    {
        var cy = Math.Cos(yaw);
        var sy = Math.Sin(yaw);
        var cp = Math.Cos(pitch);
        var sp = Math.Sin(pitch);

        var forward = new Vec3d(-sy * cp, sp, -cy * cp);
        var right = new Vec3d(cy, 0, -sy);
        var up = Vec3d.Cross(right, forward);
        return (forward, right, up);
    }

    public static (double Yaw, double Pitch) AnglesForDirection(Vec3d direction)
    {
        var dir = direction.Normalized();
        var yaw = Math.Atan2(-dir.X, -dir.Z);
        var pitch = Math.Asin(Math.Clamp(dir.Y, -1, 1));
        return (yaw, pitch);
    }

    public static void StepPlayer(ShipState ship, InputState input, double dt)
    {
        if (!ship.IsPlayer)
            throw new ArgumentException("StepPlayer requires a player ship.", nameof(ship));

        input = input.Clamped();
        var (forward, _, up) = Basis(ship.Yaw, ship.Pitch);

        var targetThrottle = (double)input.Throttle;
        ship.Throttle = MoveTowards(ship.Throttle, targetThrottle, ThrottleResponse * dt);
        ship.Boosting = input.Boost && ship.Throttle > 0.05;
        ship.Braking = input.Brake;

        ship.Yaw = WrapAngle(ship.Yaw + input.Steer * YawRate * dt);
        ship.Pitch = Math.Clamp(ship.Pitch + input.Pitch * PitchRate * dt, -MaxPitch, MaxPitch);
        (forward, _, up) = Basis(ship.Yaw, ship.Pitch);

        var throttle = ship.Throttle;
        var primaryAccel = throttle >= 0
            ? throttle * Acceleration * (ship.Boosting ? 2.2 : 1.0)
            : throttle * Acceleration * ReverseFactor;
        ship.Velocity += forward * (primaryAccel * dt);
        ship.Velocity += up * (input.Vertical * VerticalAcceleration * dt);

        // Light flight assist keeps the arcade model readable without removing inertia.
        if (throttle > 0.05 && ship.Velocity.LengthSquared > 1)
        {
            var speed = ship.Velocity.Length;
            var desired = forward * speed;
            var blend = Math.Clamp(1.2 * throttle * dt, 0, 1);
            ship.Velocity = Vec3d.Lerp(ship.Velocity, desired, blend);
        }

        if (input.Brake)
            ship.Velocity *= Math.Exp(-BrakeDrag * dt);

        ship.Velocity *= 1.0 / (1.0 + LinearDrag * dt);

        var maxSpeed = ship.Boosting ? WorldCatalog.MaxBoostSpeed : WorldCatalog.MaxSpeed;
        if (ship.Velocity.Length > maxSpeed)
            ship.Velocity = ship.Velocity.Normalized() * maxSpeed;

        ship.Position += ship.Velocity * dt;
    }

    public static void StepNpc(ShipState ship, double dt)
    {
        var route = ship.Route;
        if (route is null || route.Waypoints.Count == 0)
            return;

        for (var guard = 0; guard < 4; guard++)
        {
            var target = route.Waypoints[route.Index % route.Waypoints.Count];
            var toTarget = target - ship.Position;
            var distance = toTarget.Length;
            if (distance > 12.0)
            {
                var dir = toTarget / distance;
                var speed = route.Speed;
                var step = Math.Min(speed * dt, distance);
                ship.Position += dir * step;
                ship.Velocity = dir * speed;
                (ship.Yaw, ship.Pitch) = AnglesForDirection(dir);
                ship.Throttle = 0.75;
                return;
            }

            route.Index = (route.Index + 1) % route.Waypoints.Count;
        }
    }

    private static double MoveTowards(double current, double target, double maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
            return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    private static double WrapAngle(double angle)
    {
        while (angle > Math.PI) angle -= Math.PI * 2;
        while (angle < -Math.PI) angle += Math.PI * 2;
        return angle;
    }
}
