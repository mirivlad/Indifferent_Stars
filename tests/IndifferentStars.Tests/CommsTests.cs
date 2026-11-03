using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class CommsTests
{
    [Fact]
    public void ContactRejectsUnknownOrDistantTargets()
    {
        var catalog = WorldCatalog.Generate(12345);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Caller");
        Assert.False(world.Contact(uint.MaxValue, (uint)catalog.Station.Id).Success);
        Assert.False(world.Contact(ship.Id, (uint)catalog.Planet.Id).Success);
        ship.Position = new Vec3d(0, WorldCatalog.DistantSensorRange + 1000, 0);
        world.Npcs[0].TransponderEnabled = false;
        Assert.False(world.Contact(ship.Id, (uint)catalog.Station.Id).Success);
        Assert.False(world.Contact(ship.Id, world.Npcs[0].Id).Success);
    }

    [Fact]
    public void LocalRepliesAndRadioFitThePacketBudgetInBothLanguages()
    {
        var catalog = WorldCatalog.Generate(12345);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Caller");
        foreach (var reply in new[]
        {
            world.Contact(ship.Id, (uint)catalog.Station.Id),
            world.Contact(ship.Id, world.Npcs[0].Id),
            world.RadioBroadcast(world.Npcs[0]),
        })
        {
            Assert.True(reply.Success);
            Assert.NotEqual(reply.Message, reply.RussianMessage);
            var packet = ProtocolCodec.Encode(reply);
            Assert.True(ProtocolCodec.TryDecode(packet, out var decoded, out var error), error);
            Assert.Equal(reply, decoded);
        }
    }
}
