using System;

namespace IndifferentStars.Shared.World;

/// <summary>Chooses a nearby render origin in sector metres without changing world state.</summary>
public static class RenderFrame
{
    public const double RebaseDistance = 10_000;

    public static Vec3d ChooseOrigin(Vec3d observer, Vec3d current)
    {
        if (Vec3d.Distance(observer, current) < RebaseDistance)
            return current;
        return new Vec3d(Math.Round(observer.X / RebaseDistance) * RebaseDistance,
            Math.Round(observer.Y / RebaseDistance) * RebaseDistance,
            Math.Round(observer.Z / RebaseDistance) * RebaseDistance);
    }
}
