using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class WarpSafetyTests
{
    private static (GameWorld World, ShipState Ship, StaticBody Target) Create(bool obstructed, int distance)
    {
        var generated = WorldCatalog.Generate(12345);
        var target = new StaticBody { Id = 1000, Name = "Target", Kind = BodyKind.Asteroid, Position = new Vec3d(100_000, 0, 0), Radius = 50 };
        var blocker = new StaticBody { Id = 1001, Name = "Blocker", Kind = BodyKind.Asteroid, Position = new Vec3d(100_050 + distance, 0, 0), Radius = 30 };
        var catalog = new WorldCatalog
        {
            Seed = generated.Seed, SystemName = generated.SystemName, SectorId = generated.SectorId,
            SectorKind = generated.SectorKind, LocalFrameOrigin = generated.LocalFrameOrigin,
            Star = generated.Star, Planet = generated.Planet, Station = generated.Station,
            Asteroids = obstructed ? new[] { target, blocker } : new[] { target },
        };
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Warp test");
        ship.Position = new Vec3d(115_000, 0, 0);
        return (world, ship, target);
    }

    [Theory]
    [InlineData((ushort)500)]
    [InlineData((ushort)1000)]
    [InlineData((ushort)5000)]
    public void ArrivalDistanceIsMeasuredFromSurface(ushort distance)
    {
        var (world, ship, target) = Create(false, distance);
        var result = world.Warp(ship.Id, (uint)target.Id, distance);
        Assert.True(result.Success, result.Message);
        Assert.Equal(distance, Vec3d.Distance(result.NewPosition, target.Position) - target.Radius, 6);
    }

    [Fact]
    public void UnsafeArrivalAndInvalidDistanceDoNotMoveShip()
    {
        var (world, ship, target) = Create(true, 1000);
        var before = ship.Position;
        Assert.False(world.Warp(ship.Id, (uint)target.Id, 1000).Success);
        Assert.Equal(before, ship.Position);
        Assert.False(world.Warp(ship.Id, (uint)target.Id, 777).Success);
        Assert.Equal(before, ship.Position);
    }
}
