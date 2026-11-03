using System;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

/// <summary>Bounded flight controller in the system XZ plane; gravity is not simulated.</summary>
public static class OrbitPilot
{
    public static void Step(ShipState ship, Vec3d center, double radius, double dt)
    {
        var offset = ship.Position - center;
        var planar = new Vec3d(offset.X, 0, offset.Z);
        var radial = planar.Normalized();
        var tangent = Vec3d.Cross(ship.OrbitRetrograde ? -Vec3d.Up : Vec3d.Up, radial);
        var speed = Math.Min(10_000, Math.Sqrt(FlightModel.VerticalAcceleration * .25 * radius));
        var desired = tangent * speed + radial * Math.Clamp((radius - planar.Length) * .5, -500, 500)
            - Vec3d.Up * Math.Clamp(offset.Y * .5, -500, 500);
        var acceleration = (desired - ship.Velocity) * .8 - radial * (speed * speed / radius);
        if (acceleration.Length > FlightModel.VerticalAcceleration)
            acceleration = acceleration.Normalized() * FlightModel.VerticalAcceleration;
        ship.Velocity += acceleration * dt;
        ship.Position += ship.Velocity * dt;
        var (yaw, pitch) = FlightModel.AnglesForDirection(ship.Velocity.Normalized());
        ship.Orientation = Quaternion.Slerp(ship.Orientation,
            Quaternion.CreateFromYawPitchRoll((float)yaw, (float)pitch, 0), (float)(1 - Math.Exp(-2 * dt)));
        ship.Throttle = Math.Clamp(acceleration.Length / FlightModel.Acceleration, 0, 1);
        ship.Cruising = ship.Velocity.Length > WorldCatalog.MaxBoostSpeed;
        ship.CruiseBraking = false; ship.CruiseInhibited = false; ship.FlightAssist = true;
        ship.Boosting = false; ship.Braking = false; ship.AngularVelocity = Vec3d.Zero;
    }
}
