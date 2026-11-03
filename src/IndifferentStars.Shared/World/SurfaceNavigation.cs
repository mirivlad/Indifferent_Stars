using System;

namespace IndifferentStars.Shared.World;

public readonly record struct SurfaceNavigation(double DistanceMetres, double ClosingMetresPerSecond)
{
    public static (CelestialBody Body, double DistanceMetres) Nearest(WorldCatalog catalog, Vec3d observerSystemPosition)
    {
        var planetDistance=Measure(observerSystemPosition,Vec3d.Zero,catalog.Planet).DistanceMetres;
        var starDistance=Measure(observerSystemPosition,Vec3d.Zero,catalog.Star).DistanceMetres;
        return planetDistance<=starDistance?(catalog.Planet,planetDistance):(catalog.Star,starDistance);
    }

    public static SurfaceNavigation Measure(Vec3d observerSystemPosition, Vec3d velocity, CelestialBody body)
    {
        var offset = body.SystemPosition - observerSystemPosition;
        return new SurfaceNavigation(Math.Max(0, offset.Length - body.Radius), Vec3d.Dot(velocity, offset.Normalized()));
    }
}
