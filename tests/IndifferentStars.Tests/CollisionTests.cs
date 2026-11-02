using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class CollisionTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(20000)]
    public void StationStopsFastShipWithoutTunneling(double speed)
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var ship = world.AddPlayer("Impact");
        ship.Position = new Vec3d(0, 0, 180);
        ship.Velocity = new Vec3d(0, 0, -speed);
        world.Tick(0.05);
        Assert.InRange(ship.Position.Z, 160, 180);
        var surfaceVelocity = Vec3d.Cross(new Vec3d(0, ModelPose.StationSpin, 0), ship.Position);
        Assert.True(Vec3d.Distance(surfaceVelocity, ship.Velocity) < 1e-8);
        Assert.False(ship.Cruising);
    }

    [Fact]
    public void AsteroidAndPlanetSurfaceAreSolid()
    {
        var catalog = WorldCatalog.Generate(12345);
        foreach (var (center, radius) in new[]
        {
            (catalog.Asteroids[0].Position, catalog.Asteroids[0].Radius),
            (catalog.CelestialOffset(catalog.Planet), catalog.Planet.Radius),
            (catalog.CelestialOffset(catalog.Star), catalog.Star.Radius),
        })
        {
            var world = new GameWorld(catalog);
            var ship = world.AddPlayer("Impact");
            ship.Position = center + Vec3d.Up * (radius + 30);
            ship.Velocity = -Vec3d.Up * 20000;
            world.Tick(0.05);
            Assert.True(Vec3d.Distance(ship.Position, center) >= radius - 0.01);
            Assert.Equal(Vec3d.Zero, ship.Velocity);
        }
    }

    [Fact]
    public void OpposingShipsCannotCrossBetweenTicks()
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var a = world.AddPlayer("A");
        var b = world.AddPlayer("B");
        a.Position = new Vec3d(-100, 100000, 0);
        b.Position = new Vec3d(100, 100000, 0);
        a.Velocity = new Vec3d(20000, 0, 0);
        b.Velocity = new Vec3d(-20000, 0, 0);
        world.Tick(0.05);
        Assert.True(a.Position.X < b.Position.X);
        Assert.True(Vec3d.Distance(a.Position, b.Position) > 1);
        Assert.Equal(Vec3d.Zero, a.Velocity);
        Assert.Equal(Vec3d.Zero, b.Velocity);
    }

    [Fact]
    public void NearMissAndMovingAwayDoNotStopShip()
    {
        Assert.Null(CollisionSolver.ContactTime(new Vec3d(-1000, 21, 0), new Vec3d(1000, 21, 0), Vec3d.Zero, 20));
        Assert.Null(CollisionSolver.ContactTime(new Vec3d(20, 0, 0), new Vec3d(100, 0, 0), Vec3d.Zero, 20));
        Assert.Null(CollisionSolver.ContactTime(Vec3d.Zero, Vec3d.Zero, Vec3d.Zero, 20));
    }

    [Fact]
    public void ImpactCancelsAutopilotInsteadOfReportingArrival()
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var ship = world.AddPlayer("Impact");
        ship.Position = new Vec3d(0, 0, 180);
        ship.Velocity = new Vec3d(0, 0, -20000);
        Assert.True(world.StartMove(ship.Id, new Vec3d(0, 0, -1000)));
        world.Tick(0.05);
        Assert.Equal(0u, ship.ApproachTargetId);
        Assert.False(ship.Docked);
        var surfaceVelocity = Vec3d.Cross(new Vec3d(0, ModelPose.StationSpin, 0), ship.Position);
        Assert.True(Vec3d.Distance(surfaceVelocity, ship.Velocity) < 1e-8);
    }
}
