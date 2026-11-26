using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class HullTests
{
    [Fact]
    public void HullDefinitionDrivesPhysicsCargoCollisionAndSnapshot()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var courier = world.AddPlayer("Courier");
        var definition = Assert.IsType<HullDefinition>(HullCatalog.Find("tern"));
        var freighter = world.AddPlayer("Freighter", definition.Id);
        var input = InputState.None with { Throttle = 1, Roll = 1 };
        FlightModel.StepPlayer(courier, input, .05);
        FlightModel.StepPlayer(freighter, input, .05);
        Assert.True(freighter.Velocity.Length < courier.Velocity.Length);
        Assert.True(Math.Abs(freighter.Orientation.Z) != Math.Abs(courier.Orientation.Z));
        Assert.True(freighter.MassKg > courier.MassKg);
        Assert.True(world.GetMarket(freighter.Id)!.CargoCapacity > world.GetMarket(courier.Id)!.CargoCapacity);
        var pose = WorldSurfaces.Ship(freighter, freighter.Position, freighter.Orientation);
        Assert.Same(definition.Surface, pose.Mesh);
        Assert.NotSame(courier.Hull.Surface, pose.Mesh);
        Assert.Equal(definition.Id, world.BuildSnapshot().Single(ship => ship.Id == freighter.Id).HullId);

    }

    [Fact]
    public void AllHullDimensionsMatchTheCollisionVertices()
    {
        foreach (var definition in HullCatalog.All)
        {
            var vertices = definition.Surface.GetVertices().ToArray();
            var bounds = new Vec3d(
                vertices.Max(v => v.X) - vertices.Min(v => v.X),
                vertices.Max(v => v.Y) - vertices.Min(v => v.Y),
                vertices.Max(v => v.Z) - vertices.Min(v => v.Z));
            Assert.InRange(Vec3d.Distance(bounds, new Vec3d(definition.Dimensions[0], definition.Dimensions[1],
                definition.Dimensions[2])), 0, .03);
        }
    }

    [Fact]
    public void ExplorersAndMinersUseTheirOwnHullAndSensors()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var miner = world.Npcs.First(ship => ship.Work!.Occupation == NpcOccupation.Miner);
        var surveyor = world.Npcs.First(ship => ship.Work!.Occupation == NpcOccupation.Surveyor);
        Assert.Equal("mole", miner.Hull.Key);
        Assert.Equal("petrel", surveyor.Hull.Key);
        Assert.Equal(10, miner.Hull.NpcLoad);

        var courier = world.AddPlayer("Pilot");
        var target = world.AddPlayer("Passive target");
        courier.Position = surveyor.Position;
        target.Position = surveyor.Position + new Vec3d(4_000_000, 0, 0);
        target.TransponderEnabled = false;
        Assert.Null(world.Detect(courier, target));
        Assert.Equal(ContactSource.Tactical, world.Detect(surveyor, target));
    }

    [Fact]
    public void FreightWorkersCarryTheirHullLoadAndReduceRealStock()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101, 7));
        var tern = HullCatalog.Find("tern")!;
        var workers = world.Npcs.Where(ship => ship.HullId == tern.Id).ToArray();
        Assert.NotEmpty(workers);
        var ship = workers[0];
        var work = ship.Work!;
        Assert.Equal(NpcOccupation.Courier, work.Occupation);
        var station = world.Catalog.Find(work.HomeStationId)!;
        Assert.Equal(StationPurpose.Freight, station.Purpose);
        Assert.Equal(tern.Id, HullCatalog.ForWorker(work.Occupation, station.Purpose, ship.Id));
        ship.Position = station.Position + IndifferentStars.Shared.Collision.ModelPose.StationDockingOffset;
        ship.DockingStationId = (uint)station.Id;
        ship.Docked = true;
        work.Phase = NpcPhase.Loading;
        work.Until = 0;
        var before = world.StationStock(station.Id, 2);

        world.Tick(.05);

        Assert.True(ship.RouteActive);
        Assert.Equal(tern.NpcLoad, work.Cargo);
        Assert.Equal(before - work.Cargo, world.StationStock(station.Id, work.CommodityId));
    }
}
