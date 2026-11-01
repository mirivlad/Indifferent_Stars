using System;

namespace IndifferentStars.Simulation;

/// <summary>Normalized player intent for one simulation step.</summary>
public readonly record struct InputState(
    float Throttle,
    float Steer,
    float Pitch,
    float Vertical,
    bool Boost,
    bool Brake)
{
    public static readonly InputState None = new(0, 0, 0, 0, false, false);

    public InputState Clamped() => new(
        Math.Clamp(Throttle, -1f, 1f),
        Math.Clamp(Steer, -1f, 1f),
        Math.Clamp(Pitch, -1f, 1f),
        Math.Clamp(Vertical, -1f, 1f),
        Boost,
        Brake);

    public static InputState FromWire(sbyte throttle, sbyte steer, sbyte pitch, sbyte vertical, bool boost, bool brake) =>
        new InputState(throttle / 127f, steer / 127f, pitch / 127f, vertical / 127f, boost, brake).Clamped();
}