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
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public Vector3 Velocity;
        public sbyte Throttle;
    }

    private readonly List<Point> _points = new(8);

    public Vector3 Position { get; private set; }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public float Throttle { get; private set; }
    public bool HasData => _points.Count > 0;
    public string Name { get; private set; } = string.Empty;
    public EntityKind Kind { get; private set; }

    public static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);

    public void Push(EntitySnapshot snapshot, double receivedAt)
    {
        Name = snapshot.Name;
        Kind = snapshot.Kind;
        var position = ToVector3(snapshot.Position);

        if (_points.Count > 0)
        {
            var last = _points[^1];
            if (receivedAt <= last.Time)
                receivedAt = last.Time + 0.001;
            if (position.DistanceTo(last.Position) > 2500f)
                _points.Clear();
        }

        _points.Add(new Point
        {
            Time = receivedAt,
            Position = position,
            Yaw = snapshot.Yaw,
            Pitch = snapshot.Pitch,
            Velocity = ToVector3(snapshot.Velocity),
            Throttle = snapshot.Throttle,
        });

        while (_points.Count > 10)
            _points.RemoveAt(0);
    }

    public void Sample(double renderTime)
    {
        if (_points.Count == 0)
            return;

        if (_points.Count == 1)
        {
            Apply(_points[0]);
            return;
        }

        if (renderTime <= _points[0].Time)
        {
            Apply(_points[0]);
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
            Position = a.Position.Lerp(b.Position, t);
            Yaw = LerpAngle(a.Yaw, b.Yaw, t);
            Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t);
            Velocity = a.Velocity.Lerp(b.Velocity, t);
            Throttle = Mathf.Lerp(a.Throttle / 127f, b.Throttle / 127f, t);
            return;
        }

        var last = _points[^1];
        var extrapolation = Math.Clamp(renderTime - last.Time, 0, 0.15);
        Position = last.Position + last.Velocity * (float)extrapolation;
        Yaw = last.Yaw;
        Pitch = last.Pitch;
        Velocity = last.Velocity;
        Throttle = last.Throttle / 127f;
    }

    public void Reset(Vec3d position, float yaw, float pitch)
    {
        _points.Clear();
        Position = ToVector3(position);
        Yaw = yaw;
        Pitch = pitch;
        Throttle = 0;
        _points.Add(new Point
        {
            Time = Time.GetTicksMsec() / 1000.0,
            Position = Position,
            Yaw = yaw,
            Pitch = pitch,
        });
    }

    public Vector3 Velocity { get; private set; }

    private void Apply(Point point)
    {
        Position = point.Position;
        Yaw = point.Yaw;
        Pitch = point.Pitch;
        Velocity = point.Velocity;
        Throttle = point.Throttle / 127f;
    }

    private static float LerpAngle(float a, float b, float t)
    {
        var delta = Mathf.Wrap(b - a, -Mathf.Pi, Mathf.Pi);
        return a + delta * t;
    }
}