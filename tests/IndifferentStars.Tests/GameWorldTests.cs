using System;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
using Xunit;

namespace IndifferentStars.Tests;

public sealed class GameWorldTests
{
    private static (GameWorld World, ShipState Ship) NewWorldWithPlayer()
    {
        var world = new GameWorld(WorldCatalog.Generate(777));
        return (world, world.AddPlayer("Tester"));
    }

    [Fact]
    public void StationIsWithinStartingScanRange()
    {
        var (world, ship) = NewWorldWithPlayer();
        var result = world.Scan(ship.Id, (uint)world.Catalog.Station.Id);

        Assert.True(result.Success, result.Body);
        Assert.Contains("Kestrel Reach", result.Title);
        Assert.Contains("Traffic control", result.Body);
    }

    [Fact]
    public void ScanRejectsUnknownShipAndOutOfRangeTarget()
    {
        var (world, _) = NewWorldWithPlayer();

        var unknown = world.Scan(99999, (uint)world.Catalog.Station.Id);
        Assert.False(unknown.Success);
        Assert.Contains("unowned", unknown.Body, StringComparison.OrdinalIgnoreCase);

        var farAsteroid = world.Catalog.Asteroids
            .OrderByDescending(a => Vec3d.Distance(a.Position, world.Catalog.Station.Position))
            .First();
        var result = world.Scan(world.Players.Keys.First(), (uint)farAsteroid.Id);
        Assert.False(result.Success);
        Assert.Contains("out of range", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AsteroidScanProducesStableComposition()
    {
        var (world, ship) = NewWorldWithPlayer();
        var asteroid = world.Catalog.Asteroids
            .OrderBy(a => Vec3d.Distance(a.Position, ship.Position))
            .First();

        ship.Position = asteroid.Position + new Vec3d(0, 0, asteroid.Radius + 120);
        var first = world.Scan(ship.Id, (uint)asteroid.Id);
        var second = world.Scan(ship.Id, (uint)asteroid.Id);

        Assert.True(first.Success, first.Body);
        Assert.Equal(first.Body, second.Body);
        Assert.Contains("Composition", first.Body);
    }

    [Fact]
    public void WarpRequiresRangeAndCooldown()
    {
        var (world, ship) = NewWorldWithPlayer();
        var planetId = (uint)world.Catalog.Planet.Id;

        var first = world.Warp(ship.Id, planetId);
        Assert.True(first.Success, first.Message);
        Assert.True(Vec3d.Distance(first.NewPosition, world.Catalog.Planet.Position) < 5000);

        var second = world.Warp(ship.Id, planetId);
        Assert.False(second.Success);
        Assert.Contains("recharging", second.Message, StringComparison.OrdinalIgnoreCase);

        Assert.False(world.Warp(123456, planetId).Success);
        Assert.False(world.Warp(ship.Id, 999999).Success);
    }

    [Fact]
    public void WarpNeverMovesAnUnownedShip()
    {
        var (world, ship) = NewWorldWithPlayer();
        var before = ship.Position;
        var result = world.Warp(ship.Id + 100, (uint)world.Catalog.Planet.Id);

        Assert.False(result.Success);
        Assert.Equal(before.X, ship.Position.X);
        Assert.Equal(before.Y, ship.Position.Y);
        Assert.Equal(before.Z, ship.Position.Z);
    }

    [Fact]
    public void SnapshotContainsPlayersAndNpcsButNoAsteroidField()
    {
        var (world, ship) = NewWorldWithPlayer();
        var snapshot = world.BuildSnapshot();

        Assert.Contains(snapshot, e => e.Id == ship.Id && e.Kind == EntityKind.Player);
        Assert.Contains(snapshot, e => e.Kind == EntityKind.Npc);
        Assert.True(world.Npcs.Count >= 5);
        Assert.DoesNotContain(snapshot, e => e.Id >= 1000 && e.Id < 5000);

        var encoded = ProtocolCodec.Encode(new SnapshotMessage(1, 1234, snapshot));
        Assert.True(encoded.Length <= ProtocolConstants.MaxPacketSize);
    }

    [Fact]
    public void NpcMovementIsRepeatableOnTheServer()
    {
        var a = new GameWorld(WorldCatalog.Generate(9));
        var b = new GameWorld(WorldCatalog.Generate(9));
        for (var i = 0; i < 500; i++)
        {
            a.Tick(0.05);
            b.Tick(0.05);
        }

        Assert.Equal(a.Npcs.Count, b.Npcs.Count);
        for (var i = 0; i < a.Npcs.Count; i++)
        {
            Assert.Equal(a.Npcs[i].Position.X, b.Npcs[i].Position.X, 8);
            Assert.Equal(a.Npcs[i].Position.Y, b.Npcs[i].Position.Y, 8);
            Assert.Equal(a.Npcs[i].Position.Z, b.Npcs[i].Position.Z, 8);
        }
    }

    [Fact]
    public void RemovingPlayerDropsShipFromSnapshots()
    {
        var (world, ship) = NewWorldWithPlayer();
        Assert.True(world.RemovePlayer(ship.Id));
        Assert.DoesNotContain(world.BuildSnapshot(), e => e.Id == ship.Id);
        Assert.False(world.ApplyInputThenExists(ship.Id));
        Assert.Null(world.GetPlayer(ship.Id));
    }
}

internal static class GameWorldTestExtensions
{
    public static bool ApplyInputThenExists(this GameWorld world, uint playerId)
    {
        world.ApplyInput(playerId, InputState.None);
        return world.GetPlayer(playerId) is not null;
    }
}