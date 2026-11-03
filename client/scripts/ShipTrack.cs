using System;
using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

/// <summary>
/// Interpolates authoritative snapshots on the render timeline. The client never
/// simulates ship positions; it only smooths the samples received from the server.
/// </summary>
public sealed class ShipTrack
{
    private struct Point
    {
        public double Time;
        public ulong? ServerMs;
        public Vec3d Position;
        public float Yaw;
        public float Pitch;
        public Quaternion Orientation;
        public Vector3 Velocity;
        public sbyte Throttle;
    }

    private readonly List<Point> _points = new(8);

    public Vector3 Position { get; private set; }
    public Vector3 RenderPosition { get; private set; }
    public double SampledServerSeconds { get; private set; }
    public Vec3d WorldPosition { get; private set; }
    public Vec3d LatestPosition => _points.Count > 0 ? _points[^1].Position : Vec3d.Zero;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public float Throttle { get; private set; }
    public ImpactState? Impact { get; private set; }
    public uint RouteRevision { get; private set; }
    public byte RouteIndex { get; private set; }
    public bool RouteActive { get; private set; }
    public bool Boosting { get; private set; }
    public uint OrbitTargetId { get; private set; }
    public double OrbitAltitude { get; private set; }
    public bool OrbitRetrograde { get; private set; }
    public NpcStatus? Activity { get; private set; }
    public uint DockingStationId { get; private set; }
    public bool OrbitEstablished { get; private set; }
    public bool HasData => _points.Count > 0;
    public string Name { get; private set; } = string.Empty;
    public EntityKind Kind { get; private set; }
    public uint ApproachTargetId { get; private set; }
    public bool Docked { get; private set; }
    public bool DockingGranted { get; private set; }
    public bool FlightAssist { get; private set; }
    public bool ApproachFollowing { get; private set; }
    public WarpStage WarpStage { get; private set; }
    public Vec3d? WarpDestination { get; private set; }
    public float WarpProgress { get; private set; }
    public bool Cruising { get; private set; }
    public Vec3d? ApproachPoint { get; private set; }
    public bool CruiseInhibited { get; private set; }
    public bool CruiseBraking { get; private set; }

    public static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);

    public bool Push(EntitySnapshot snapshot, double receivedAt, ulong serverTimeMs)
    {
        if (_points.Count > 0 && _points[^1].ServerMs is { } latest && serverTimeMs <= latest)
            return false;
        if (_points.Count == 1 && _points[0].ServerMs is null)
            _points.Clear();
        Boosting = snapshot.Boosting;
        OrbitTargetId=snapshot.OrbitTargetId; OrbitAltitude=snapshot.OrbitAltitude;
        OrbitRetrograde=snapshot.OrbitRetrograde; OrbitEstablished=snapshot.OrbitEstablished; DockingStationId=snapshot.DockingStationId;Activity=snapshot.Activity;
        RouteRevision = snapshot.RouteRevision; RouteIndex = snapshot.RouteIndex; RouteActive = snapshot.RouteActive;
        Impact = snapshot.Impact;
        Name = snapshot.Name;
        Kind = snapshot.Kind;
        ApproachTargetId = snapshot.ApproachTargetId;
        Docked = snapshot.Docked;
        DockingGranted = snapshot.DockingGranted;
        FlightAssist = snapshot.FlightAssist;
        ApproachFollowing = snapshot.ApproachFollowing;
        var previousWarp = WarpStage;
        WarpStage = snapshot.WarpStage;
        WarpProgress = snapshot.WarpProgress;
        WarpDestination = snapshot.WarpDestination;
        Cruising = snapshot.Cruising;
        ApproachPoint = snapshot.ApproachPoint;
        CruiseInhibited = snapshot.CruiseInhibited;
        CruiseBraking = snapshot.CruiseBraking;
        var position = snapshot.Position;
        var sampleTime = receivedAt;

        if (_points.Count > 0)
        {
            var last = _points[^1];
            // Timestamp samples by the server's own clock rather than by arrival time.
            // Two datagrams processed in the same frame would otherwise get almost the
            // same local timestamp and produce a violent speed spike between them.
            if (last.ServerMs is { } previous)
            {
                sampleTime = last.Time + (serverTimeMs - previous) / 1000.0;
                var correction = receivedAt - sampleTime;
                // Queued packets cannot put samples in the future. A long server
                // pause must also restore the buffer, rather than extrapolate forever.
                if (correction < 0 || correction > 0.25)
                {
                    for (var i = 0; i < _points.Count; i++)
                    {
                        var point = _points[i];
                        point.Time += correction;
                        _points[i] = point;
                    }
                    sampleTime = receivedAt;
                }
            }

            var interval=last.ServerMs is { } previousMs ? (serverTimeMs-previousMs)/1000.0 : Math.Max(0,sampleTime-last.Time);
            var expectedTravel=Math.Max(last.Velocity.Length(),snapshot.Velocity.Length)*interval;
            if (Vec3d.Distance(position, last.Position) > 2500 + expectedTravel &&
                !(previousWarp != WarpStage.None && WarpStage != WarpStage.None))
            {
                _points.Clear();
                sampleTime = receivedAt;
            }
        }

        _points.Add(new Point
        {
            Time = sampleTime,
            ServerMs = serverTimeMs,
            Position = position,
            Yaw = snapshot.Yaw,
            Pitch = snapshot.Pitch,
            Orientation = new Quaternion(snapshot.Orientation.X, snapshot.Orientation.Y, snapshot.Orientation.Z, snapshot.Orientation.W).Normalized(),
            Velocity = ToVector3(snapshot.Velocity),
            Throttle = snapshot.Throttle,
        });

        while (_points.Count > 10)
            _points.RemoveAt(0);
        return true;
    }

    public void Sample(double renderTime, Vec3d renderOrigin)
    {
        if (_points.Count == 0)
            return;

        if (_points.Count == 1)
        {
            Apply(_points[0], renderOrigin);
            return;
        }

        if (renderTime <= _points[0].Time)
        {
            Apply(_points[0], renderOrigin);
            return;
        }

        for (var i = 0; i < _points.Count - 1; i++)
        {
            var a = _points[i];
            var b = _points[i + 1];
            if (renderTime < a.Time)
                continue;
            if (renderTime > b.Time)
                continue;

            var t = (float)((renderTime - a.Time) / Math.Max(0.0001, b.Time - a.Time));
            if (a.ServerMs is { } serverA && b.ServerMs is { } serverB)
                SampledServerSeconds = (serverA + (serverB - serverA) * (double)t) / 1000.0;
            SetPosition(Vec3d.Lerp(a.Position, b.Position, t), renderOrigin);
            Yaw = LerpAngle(a.Yaw, b.Yaw, t);
            Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t);
            Orientation = a.Orientation.Slerp(b.Orientation, t).Normalized();
            Velocity = a.Velocity.Lerp(b.Velocity, t);
            Throttle = Mathf.Lerp(a.Throttle / 127f, b.Throttle / 127f, t);
            return;
        }

        var last = _points[^1];
        var extrapolation = Math.Clamp(renderTime - last.Time, 0, 0.15);
        if (last.ServerMs is { } lastServerMs) SampledServerSeconds = lastServerMs / 1000.0 + extrapolation;
        SetPosition(last.Position + new Vec3d(last.Velocity.X, last.Velocity.Y, last.Velocity.Z) * extrapolation, renderOrigin);
        Yaw = last.Yaw;
        Pitch = last.Pitch;
        Orientation = last.Orientation;
        Velocity = last.Velocity;
        Throttle = last.Throttle / 127f;
    }

    public void Reset(Vec3d position, float yaw, float pitch)
    {
        _points.Clear();
        SampledServerSeconds = 0;
        WarpStage = WarpStage.None;
        WarpProgress = 0;
        WarpDestination = null;
        Cruising = false;
        CruiseInhibited = false;
        CruiseBraking = false;
        WorldPosition = position;
        Position = ToVector3(position);
        Yaw = yaw;
        Pitch = pitch;
        Orientation = ShipView.BasisFromAngles(yaw, pitch).GetRotationQuaternion();
        Throttle = 0;
        _points.Add(new Point
        {
            Time = Time.GetTicksUsec() / 1_000_000.0,
            Position = position,
            Yaw = yaw,
            Pitch = pitch,
            Orientation = Orientation,
        });
    }

    public Vector3 Velocity { get; private set; }

    private void Apply(Point point, Vec3d renderOrigin)
    {
        if (point.ServerMs is { } serverMs) SampledServerSeconds = serverMs / 1000.0;
        SetPosition(point.Position, renderOrigin);
        Yaw = point.Yaw;
        Pitch = point.Pitch;
        Orientation = point.Orientation;
        Velocity = point.Velocity;
        Throttle = point.Throttle / 127f;
    }

    private void SetPosition(Vec3d position, Vec3d renderOrigin)
    {
        WorldPosition = position;
        Position = ToVector3(position);
        RenderPosition = ToVector3(position - renderOrigin);
    }

    private static float LerpAngle(float a, float b, float t)
    {
        var delta = Mathf.Wrap(b - a, -Mathf.Pi, Mathf.Pi);
        return a + delta * t;
    }
}
