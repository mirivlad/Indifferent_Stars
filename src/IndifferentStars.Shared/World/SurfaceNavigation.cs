using System;

namespace IndifferentStars.Shared.World;

public readonly record struct SurfaceNavigation(double DistanceMetres, double ClosingMetresPerSecond)
{
    public static (CelestialBody Body, double DistanceMetres) Nearest(WorldCatalog catalog, Vec3d observerSystemPosition)
    {
        var nearest=catalog.Star;var distance=double.MaxValue;
        foreach(var body in catalog.Celestials)
        {
            var gap=Measure(observerSystemPosition,Vec3d.Zero,body).DistanceMetres;
            if(gap<distance){distance=gap;nearest=body;}
        }
        return(nearest,distance);
    }

    public static SurfaceNavigation Measure(Vec3d observerSystemPosition, Vec3d velocity, CelestialBody body)
    {
        var offset = body.SystemPosition - observerSystemPosition;
        return new SurfaceNavigation(Math.Max(0, offset.Length - body.Radius), Vec3d.Dot(velocity, offset.Normalized()));
    }
}
