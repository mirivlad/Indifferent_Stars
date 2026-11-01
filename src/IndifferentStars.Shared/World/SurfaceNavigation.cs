using System;

namespace IndifferentStars.Shared.World;

public readonly record struct SurfaceNavigation(double DistanceMetres, double ClosingMetresPerSecond)
{
    public static SurfaceNavigation Measure(Vec3d observerSystemPosition, Vec3d velocity, CelestialBody body)
    {
        var offset = body.SystemPosition - observerSystemPosition;
        return new SurfaceNavigation(Math.Max(0, offset.Length - body.Radius), Vec3d.Dot(velocity, offset.Normalized()));
    }
}
