using System;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

/// <summary>Generates ordinary flight inputs towards a stationary arrival point.</summary>
public static class ApproachPilot
{
    public static InputState Steer(ShipState ship, Vec3d destination, Vec3d targetVelocity = default, double maxSpeed = WorldCatalog.MaxSpeed * 0.65)
    {
        var offset = destination - ship.Position;
        var distance = offset.Length;
        var direction = offset.Normalized();
        var desiredSpeed = Math.Min(maxSpeed, Math.Sqrt(2 * FlightModel.VerticalAcceleration * distance) * 0.55);
        var desiredAcceleration = (targetVelocity + direction * desiredSpeed - ship.Velocity) * 0.8;
        var f = Vector3.Transform(-Vector3.UnitZ, ship.Orientation);
        var r = Vector3.Transform(Vector3.UnitX, ship.Orientation);
        var u = Vector3.Transform(Vector3.UnitY, ship.Orientation);
        var forward = new Vec3d(f.X, f.Y, f.Z);
        var right = new Vec3d(r.X, r.Y, r.Z);
        var up = new Vec3d(u.X, u.Y, u.Z);
        var side = Vec3d.Dot(direction, right);
        var ahead = Vec3d.Dot(direction, forward);
        var yaw = ahead < -0.99 && Math.Abs(side) < 0.02 ? 1 : Math.Clamp(-side * 2, -1, 1);
        var pitch = Math.Clamp(Vec3d.Dot(direction, up) * 2, -1, 1);
        var forwardAccel = Vec3d.Dot(desiredAcceleration, forward);
        var throttle = forwardAccel >= 0 ? forwardAccel / FlightModel.Acceleration : forwardAccel / (FlightModel.Acceleration * FlightModel.ReverseFactor);
        return new InputState(
            (float)Math.Clamp(throttle, -1, 1), (float)yaw, (float)pitch,
            (float)Math.Clamp(Vec3d.Dot(desiredAcceleration, up) / FlightModel.VerticalAcceleration, -1, 1),
            false, false,
            (float)Math.Clamp(Vec3d.Dot(desiredAcceleration, right) / FlightModel.VerticalAcceleration, -1, 1), 0);
    }

    public static bool Arrived(ShipState ship, Vec3d destination) =>
        Vec3d.Distance(ship.Position, destination) <= 15 && ship.Velocity.Length <= 3;
}
