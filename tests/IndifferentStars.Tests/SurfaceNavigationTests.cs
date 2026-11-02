using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class SurfaceNavigationTests
{
    [Fact]
    public void HalfHourCoastTowardsPlanetHasNoSectorOrDistanceClamp()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Long flight");
        ship.Position = new Vec3d(0, 0, 1000);
        ship.Velocity = new Vec3d(0, -720, 0);
        for (var tick = 0; tick < 36_000; tick++) world.Tick(0.05);
        Assert.Equal(-1_296_000, ship.Position.Y, 3);
        Assert.Equal(new Vec3d(0, -720, 0), ship.Velocity);
        var radial = world.Catalog.Planet.Radius + WorldCatalog.StationOrbitAltitude - 1_296_000;
        var expectedAltitude = System.Math.Sqrt(radial * radial + 1_000_000) - world.Catalog.Planet.Radius;
        Assert.Equal(expectedAltitude, SurfaceNavigation.Measure(world.Catalog.LocalFrameOrigin + ship.Position,
            ship.Velocity, world.Catalog.Planet).DistanceMetres, 3);
    }

    [Fact]
    public void SurfaceDistanceAndClosingSpeedUseRealCoordinatesAndVelocity()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var start = SurfaceNavigation.Measure(catalog.LocalFrameOrigin, new Vec3d(0, -720, 0), catalog.Planet);
        Assert.Equal(1_400_000, start.DistanceMetres, 5);
        Assert.Equal(720, start.ClosingMetresPerSecond, 5);
        var afterHalfHour = catalog.LocalFrameOrigin + new Vec3d(0, -720 * 1800, 0);
        Assert.Equal(104_000, SurfaceNavigation.Measure(afterHalfHour, Vec3d.Zero, catalog.Planet).DistanceMetres, 5);
        Assert.Equal(-260, SurfaceNavigation.Measure(catalog.LocalFrameOrigin, new Vec3d(0, 260, 0), catalog.Planet).ClosingMetresPerSecond, 5);
        Assert.Equal(0, SurfaceNavigation.Measure(catalog.LocalFrameOrigin, new Vec3d(260, 0, 0), catalog.Planet).ClosingMetresPerSecond, 5);
    }
}
