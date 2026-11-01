using System;
using System.Numerics;
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
    public const double BrakeAcceleration = 300.0;
    public const double YawRate = 1.15;
    public const double PitchRate = 0.85;
    public const double RollRate = 1.5;

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
        ship.Throttle = input.Throttle;
        ship.Boosting = input.Boost && ship.Throttle > 0.05;
        ship.Braking = input.Brake;

        var rotation = Quaternion.CreateFromYawPitchRoll(
            (float)(input.Steer * YawRate * dt),
            (float)(input.Pitch * PitchRate * dt),
            (float)(input.Roll * RollRate * dt));
        ship.Orientation = Quaternion.Normalize(ship.Orientation * rotation);
        var f = Vector3.Transform(-Vector3.UnitZ, ship.Orientation);
        var r = Vector3.Transform(Vector3.UnitX, ship.Orientation);
        var u = Vector3.Transform(Vector3.UnitY, ship.Orientation);
        var forward = new Vec3d(f.X, f.Y, f.Z);
        var right = new Vec3d(r.X, r.Y, r.Z);
        var up = new Vec3d(u.X, u.Y, u.Z);

        var throttle = ship.Throttle;
        var primaryAccel = throttle >= 0
            ? throttle * Acceleration * (ship.Boosting ? 2.2 : 1.0)
            : throttle * Acceleration * ReverseFactor;
        if (input.Brake)
        {
            var speed = ship.Velocity.Length;
            ship.Velocity = speed <= BrakeAcceleration * dt
                ? Vec3d.Zero
                : ship.Velocity * ((speed - BrakeAcceleration * dt) / speed);
        }
        else
        {
            var previousSpeed = ship.Velocity.Length;
            ship.Velocity += (forward * primaryAccel + up * (input.Vertical * VerticalAcceleration) + right * (input.Strafe * VerticalAcceleration)) * dt;
            var maxSpeed = ship.Boosting ? WorldCatalog.MaxBoostSpeed : WorldCatalog.MaxSpeed;
            var allowedSpeed = Math.Max(previousSpeed, maxSpeed);
            if (ship.Velocity.Length > allowedSpeed)
                ship.Velocity = ship.Velocity.Normalized() * allowedSpeed;
        }

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
                var (yaw, pitch) = AnglesForDirection(dir);
                ship.SetAngles(yaw, pitch);
                ship.Throttle = 0.75;
                return;
            }

            route.Index = (route.Index + 1) % route.Waypoints.Count;
        }
    }

}
