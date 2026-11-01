using System;
using System.Collections.Generic;
using System.Numerics;
using IndifferentStars.Shared;

namespace IndifferentStars.Simulation;

public sealed class NpcRoute
{
    public required IReadOnlyList<Vec3d> Waypoints { get; init; }
    public required double Speed { get; init; }
    public int Index { get; set; }
}

public sealed class ShipState
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public bool IsPlayer { get; init; }
    public string Role { get; init; } = "Courier";

    public Vec3d Position { get; set; }
    public Vec3d Velocity { get; set; }
    public Quaternion Orientation { get; set; } = Quaternion.Identity;
    public double Yaw
    {
        get => FlightModel.AnglesForDirection(Forward).Yaw;
        set => SetAngles(value, Pitch);
    }
    public double Pitch
    {
        get => FlightModel.AnglesForDirection(Forward).Pitch;
        set => SetAngles(Yaw, value);
    }
    public double Throttle { get; set; }
    public bool Boosting { get; set; }
    public bool Braking { get; set; }
    public uint ApproachTargetId { get; set; }
    public Vec3d ApproachDestination { get; set; }
    public double LastWarpAtSeconds { get; set; } = -1000;
    public NpcRoute? Route { get; set; }

    public Vec3d Forward
    {
        get
        {
            var forward = Vector3.Transform(-Vector3.UnitZ, Orientation);
            return new Vec3d(forward.X, forward.Y, forward.Z);
        }
    }

    public void SetAngles(double yaw, double pitch) =>
        Orientation = Quaternion.CreateFromYawPitchRoll((float)yaw, (float)pitch, 0);
}
