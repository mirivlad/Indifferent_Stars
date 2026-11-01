using System;
using System.Collections.Generic;
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
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double Throttle { get; set; }
    public bool Boosting { get; set; }
    public bool Braking { get; set; }
    public double LastWarpAtSeconds { get; set; } = -1000;
    public NpcRoute? Route { get; set; }

    public Vec3d Forward
    {
        get
        {
            var (forward, _, _) = FlightModel.Basis(Yaw, Pitch);
            return forward;
        }
    }
}