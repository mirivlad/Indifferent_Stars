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
    public void StationReboundsFastShipWithoutTunneling(double speed)
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var ship = world.AddPlayer("Impact");
        var start = new Vec3d(0, 0, 3.18 * ModelPose.StationScale + 18);
        ship.Position = start;
        ship.Velocity = new Vec3d(0, 0, -speed);
        world.Tick(0.05);
        Assert.InRange(ship.Position.Z, start.Z - 24, start.Z);
        var surfaceVelocity = Vec3d.Cross(new Vec3d(0, ModelPose.StationSpin, 0), ship.Position);
        Assert.True(ship.Velocity.Z-surfaceVelocity.Z>1);
        Assert.NotNull(ship.Impact);
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
            Assert.True(Vec3d.Dot(ship.Velocity,(ship.Position-center).Normalized())>0);
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
        Assert.InRange(a.Position.X, -6, -3);
        Assert.InRange(b.Position.X, 3, 6);
        Assert.True(Vec3d.Distance(a.Position, b.Position) > 1);
        Assert.True(a.Velocity.X<0);
        Assert.True(b.Velocity.X>0);
    }

    [Fact]
    public void ShipCanDepartAfterAStationContact()
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var ship = world.AddPlayer("Escape");
        ship.Position = new Vec3d(0,0,3.18*ModelPose.StationScale+18);
        ship.Velocity = new Vec3d(0,0,-500);
        world.Tick(.05);
        var before=ship.Position;
        ship.Velocity = new Vec3d(0,0,40);
        world.Tick(.05);
        Assert.True(ship.Position.Z>before.Z+.5,"A separating hull must not be trapped at contact t=0.");
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
        ship.Position = new Vec3d(0, 0, 3.18 * ModelPose.StationScale + 18);
        ship.Velocity = new Vec3d(0, 0, -20000);
        Assert.True(world.StartMove(ship.Id, new Vec3d(0, 0, -1000)));
        world.Tick(0.05);
        Assert.Equal(0u, ship.ApproachTargetId);
        Assert.False(ship.Docked);
        var surfaceVelocity = Vec3d.Cross(new Vec3d(0, ModelPose.StationSpin, 0), ship.Position);
        Assert.True(ship.Velocity.Z-surfaceVelocity.Z>1);
        Assert.NotNull(ship.Impact);
    }
}
