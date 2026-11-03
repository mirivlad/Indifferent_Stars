using System;
using System.Collections.Generic;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

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
    public Vec3d AngularVelocity { get; set; }
    public double MassKg => 20_000 * (IsPlayer ? 1 : Math.Pow(IndifferentStars.Shared.Collision.ModelPose.NpcScale,3));
    public ImpactState? Impact { get; set; }
    public double ImpactAt { get; set; } = -100;
    public uint ImpactSequence { get; set; }
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
    public bool Cruising { get; set; }
    public bool CruiseInhibited { get; set; }
    public bool CruiseBraking { get; set; }
    public bool Braking { get; set; }
    public bool FlightAssist { get; set; }
    // The ship's own id marks a free waypoint; other ids refer to contacts.
    public uint ApproachTargetId { get; set; }
    public bool ApproachFollowing { get; set; }
    public Vec3d ApproachDestination { get; set; }
    public bool DockingGranted { get; set; }
    public bool Docked { get; set; }
    public Vec3d DockingPoint { get; set; }
    public WarpStage WarpStage { get; set; }
    public float WarpProgress { get; set; }
    public Vec3d WarpStart { get; set; }
    public Vec3d WarpDestination { get; set; }
    public double WarpStartedAt { get; set; }
    public Quaternion WarpInitialOrientation { get; set; }
    public Quaternion WarpOrientation { get; set; }
    public bool InWarp => WarpStage != WarpStage.None;
    public NavigationPoint[] NavigationRoute { get; set; } = Array.Empty<NavigationPoint>();
    public uint RouteRevision { get; set; }
    public byte RouteIndex { get; set; }
    public bool RouteActive { get; set; }
    public bool RouteWarping { get; set; }
    public bool RouteAdvanceAfterWarp { get; set; }
    public double RouteCruiseSeconds { get; set; }
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
