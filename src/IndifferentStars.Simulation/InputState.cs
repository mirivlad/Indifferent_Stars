using System;

namespace IndifferentStars.Simulation;

/// <summary>Normalized player intent for one simulation step.</summary>
public readonly record struct InputState(
    float Throttle,
    float Steer,
    float Pitch,
    float Vertical,
    bool Boost,
    bool Brake,
    float Strafe = 0,
    float Roll = 0,
    bool FlightAssist = false)
{
    public static readonly InputState None = new(0, 0, 0, 0, false, false);

    public InputState Clamped() => new(
        Math.Clamp(Throttle, -1f, 1f),
        Math.Clamp(Steer, -1f, 1f),
        Math.Clamp(Pitch, -1f, 1f),
        Math.Clamp(Vertical, -1f, 1f),
        Boost,
        Brake,
        Math.Clamp(Strafe, -1f, 1f),
        Math.Clamp(Roll, -1f, 1f),
        FlightAssist);

    public static InputState FromWire(sbyte throttle, sbyte steer, sbyte pitch, sbyte vertical, bool boost, bool brake, sbyte strafe, sbyte roll, bool flightAssist) =>
        new InputState(throttle / 127f, steer / 127f, pitch / 127f, vertical / 127f, boost, brake, strafe / 127f, roll / 127f, flightAssist).Clamped();
}
