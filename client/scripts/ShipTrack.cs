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
    public Vec3d WorldPosition { get; private set; }
    public Vec3d LatestPosition => _points.Count > 0 ? _points[^1].Position : Vec3d.Zero;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public float Throttle { get; private set; }
    public bool HasData => _points.Count > 0;
    public string Name { get; private set; } = string.Empty;
    public EntityKind Kind { get; private set; }
    public uint ApproachTargetId { get; private set; }
    public bool Docked { get; private set; }
    public bool DockingGranted { get; private set; }

    public static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);

    public void Push(EntitySnapshot snapshot, double receivedAt)
    {
        Name = snapshot.Name;
        Kind = snapshot.Kind;
        ApproachTargetId = snapshot.ApproachTargetId;
        Docked = snapshot.Docked;
        DockingGranted = snapshot.DockingGranted;
        var position = snapshot.Position;

        if (_points.Count > 0)
        {
            var last = _points[^1];
            if (receivedAt <= last.Time)
                receivedAt = last.Time + 0.001;
            if (Vec3d.Distance(position, last.Position) > 2500)
                _points.Clear();
        }

        _points.Add(new Point
        {
            Time = receivedAt,
            Position = position,
            Yaw = snapshot.Yaw,
            Pitch = snapshot.Pitch,
            Orientation = new Quaternion(snapshot.Orientation.X, snapshot.Orientation.Y, snapshot.Orientation.Z, snapshot.Orientation.W).Normalized(),
            Velocity = ToVector3(snapshot.Velocity),
            Throttle = snapshot.Throttle,
        });

        while (_points.Count > 10)
            _points.RemoveAt(0);
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
        WorldPosition = position;
        Position = ToVector3(position);
        Yaw = yaw;
        Pitch = pitch;
        Orientation = ShipView.BasisFromAngles(yaw, pitch).GetRotationQuaternion();
        Throttle = 0;
        _points.Add(new Point
        {
            Time = Time.GetTicksMsec() / 1000.0,
            Position = position,
            Yaw = yaw,
            Pitch = pitch,
            Orientation = Orientation,
        });
    }

    public Vector3 Velocity { get; private set; }

    private void Apply(Point point, Vec3d renderOrigin)
    {
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
