namespace IndifferentStars.Shared.World;

/// <summary>
/// A real celestial body in system coordinates. Positions are metres relative to the
/// system barycentre and radii are true metres, so planets and stars keep astronomical
/// proportions. Rendering maps them into a nearby display frame (see client
/// CelestialRenderer); gameplay never stores these huge coordinates in a float.
/// </summary>
public sealed class CelestialBody
{
    public required int Id { get; init; }
    public required CelestialKind Kind { get; init; }
    public required string Name { get; init; }

    /// <summary>Real position from the system barycentre, in metres.</summary>
    public required Vec3d SystemPosition { get; init; }

    /// <summary>Real equatorial radius, in metres.</summary>
    public required double Radius { get; init; }

    /// <summary>Axial tilt used only by the renderer, in radians.</summary>
    public float AxialTilt { get; init; }

    /// <summary>Atmosphere shell thickness in metres; 0 means airless.</summary>
    public double AtmosphereHeight { get; init; }

    /// <summary>Ring inner edge in body radii; 0 means no rings.</summary>
    public double RingInnerRadii { get; init; }

    /// <summary>Ring outer edge in body radii; 0 means no rings.</summary>
    public double RingOuterRadii { get; init; }
}

public enum CelestialKind
{
    Star,
    Planet,
    Moon,
}