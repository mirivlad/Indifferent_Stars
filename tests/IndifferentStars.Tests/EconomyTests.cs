using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class EconomyTests
{
    [Fact]
    public void SpecializationsProduceAndConsumeActualStock()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var source = new GameWorld(catalog);
        var snapshot = source.CaptureSimulation();
        var world = new GameWorld(catalog);
        world.RestoreSimulation(snapshot with { Economy = snapshot.Economy! with { NextProduction = 0 } });
        var refinery = catalog.Stations.First(station => station.Purpose == StationPurpose.Mining).Id;
        var assembly = catalog.Stations.First(station => station.Purpose == StationPurpose.Freight).Id;
        var research = catalog.Stations.First(station => station.Purpose == StationPurpose.Research).Id;
        var ore = world.StationStock(refinery, CommodityCatalog.Ore);
        var metal = world.StationStock(refinery, CommodityCatalog.Metal);
        var components = world.StationStock(assembly, CommodityCatalog.Components);

        world.Tick(.05);

        Assert.Equal(ore - 7, world.StationStock(refinery, CommodityCatalog.Ore));
        Assert.Equal(metal + 5, world.StationStock(refinery, CommodityCatalog.Metal));
        Assert.Equal(components + 4, world.StationStock(assembly, CommodityCatalog.Components));
        var ledger = world.CaptureSimulation().Economy!.Stations;
        Assert.Equal(7, ledger[refinery].Consumed[CommodityCatalog.Ore]);
        Assert.Equal(5, ledger[assembly].Consumed[CommodityCatalog.Metal]);
        Assert.Equal(2, ledger[research].Produced[CommodityCatalog.Equipment]);
        Assert.Equal(3, ledger[research].Consumed[CommodityCatalog.Components]);
        Assert.True(ledger[catalog.Station.Id].Credits > EconomyRules.InitialTreasury);
    }

    [Fact]
    public void DepletionRaisesTheQuoteAndTreasuryReceivesTheActualPrice()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var pilot = world.AddPlayer("Observer");
        pilot.Docked = true;
        var entry = world.GetMarket(pilot.Id)!.Entries.Single(item => item.CommodityId == CommodityCatalog.Water);
        var sale = world.Trade(pilot.Id, CommodityCatalog.Water, pilot.Hull.CargoCapacity, true);
        var remaining = sale.State!.Entries.Single(item => item.CommodityId == CommodityCatalog.Water);

        Assert.True(sale.Success);
        Assert.Equal(entry.StationStock - pilot.Hull.CargoCapacity, remaining.StationStock);
        Assert.True(remaining.BuyPrice > entry.BuyPrice);
        Assert.True(remaining.SellPrice > entry.SellPrice);
        Assert.Equal(CommodityCatalog.StartingCredits - pilot.Hull.CargoCapacity * entry.BuyPrice, sale.State.Credits);
        Assert.Equal(EconomyRules.InitialTreasury + pilot.Hull.CargoCapacity * entry.BuyPrice,
            world.CaptureSimulation().Economy!.Stations[world.Catalog.Station.Id].Credits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StationWithFullStorageOrNoCreditsRejectsWithoutTakingCargo(bool fullStorage)
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var pilot = world.AddPlayer("Transport");
        pilot.Docked = true;
        Assert.True(world.Trade(pilot.Id, CommodityCatalog.Equipment, 4, true).Success);
        var station = world.Catalog.Station.Id;
        var state = world.CaptureSimulation();
        if (fullStorage)
            state.StationStocks[station][CommodityCatalog.Equipment] = EconomyRules.Capacity(CommodityCatalog.Find(CommodityCatalog.Equipment)!);
        else
            state.Economy!.Stations[station].Credits = 0;
        var credits = world.GetMarket(pilot.Id)!.Credits;

        var rejected = world.Trade(pilot.Id, CommodityCatalog.Equipment, 4, false);

        Assert.False(rejected.Success);
        Assert.Equal(credits, rejected.State!.Credits);
        var item = rejected.State.Entries.Single(entry => entry.CommodityId == CommodityCatalog.Equipment);
        Assert.Equal(4, item.PlayerQuantity);
        Assert.Equal(0, item.Demand);
        Assert.Equal(state.StationStocks[station][CommodityCatalog.Equipment], item.StationStock);
    }

    [Fact]
    public void ASecondPilotCannotBeChargedAboveTheAdvertisedLimit()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var first = world.AddPlayer("Bulk pilot");
        var second = world.AddPlayer("Later pilot");
        first.Docked = true;
        var limit = world.GetMarket(second.Id)!.Entries.Single(row => row.CommodityId == CommodityCatalog.Water).BuyPrice;
        Assert.True(world.Trade(first.Id, CommodityCatalog.Water, 30, true, limit).Success);
        first.Docked = false;
        second.Docked = true;

        var rejected = world.Trade(second.Id, CommodityCatalog.Water, 1, true, limit);

        Assert.False(rejected.Success);
        Assert.Contains("changed", rejected.Message);
        Assert.Equal(0, rejected.State!.CargoUsed);
        Assert.Equal(CommodityCatalog.StartingCredits, rejected.State.Credits);
        Assert.True(rejected.State.Entries.Single(row => row.CommodityId == CommodityCatalog.Water).BuyPrice > limit);
    }

    [Fact]
    public void AFullOutputWarehouseDoesNotBurnRefineryInputs()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var state = new GameWorld(catalog).CaptureSimulation();
        var station = catalog.Stations.First(body => body.Purpose == StationPurpose.Mining).Id;
        var storage = state.StationStocks[station];
        storage[CommodityCatalog.Metal] = EconomyRules.Capacity(CommodityCatalog.Find(CommodityCatalog.Metal)!);
        var ore = storage[CommodityCatalog.Ore];
        var fuel = storage[CommodityCatalog.Fuel];
        var world = new GameWorld(catalog);
        world.RestoreSimulation(state with { Economy = state.Economy! with { NextProduction = 0 } });

        world.Tick(.05);

        Assert.Equal(ore, world.StationStock(station, CommodityCatalog.Ore));
        Assert.Equal(fuel, world.StationStock(station, CommodityCatalog.Fuel));
        Assert.Equal(storage[CommodityCatalog.Metal], world.StationStock(station, CommodityCatalog.Metal));
        Assert.Empty(world.CaptureSimulation().Economy!.Stations[station].Produced);
    }

    [Fact]
    public void ADestroyedShipmentLeavesTheBuyerWithLessStockAndAHigherQuote()
    {
        var delivered = ShipCargo(destroyed: false);
        var lost = ShipCargo(destroyed: true);
        Assert.Equal(delivered.CommodityId, lost.CommodityId);
        Assert.True(lost.StationStock < delivered.StationStock);
        Assert.True(lost.SellPrice > delivered.SellPrice);

        static MarketEntry ShipCargo(bool destroyed)
        {
            var catalog = WorldCatalog.Generate(20261101);
            var world = new GameWorld(catalog);
            var hauler = world.Npcs.First(npc => npc.Work!.Occupation == NpcOccupation.Courier);
            var work = hauler.Work!;
            hauler.Docked = true;
            hauler.DockingStationId = (uint)catalog.Station.Id;
            hauler.Position = catalog.Station.Position + ModelPose.StationDockingOffset;
            work.Until = 0;
            world.Tick(.05);
            Assert.True(work.Cargo > 0);
            var commodity = work.CommodityId;
            var quantity = work.Cargo;
            var destination = catalog.Find(work.TargetId)!;
            var observer = world.AddPlayer("Buyer");
            observer.Position = destination.Position + ModelPose.StationDockingOffset;
            Assert.True(world.RequestDocking(observer.Id, (uint)destination.Id));
            world.Tick(.05);
            Assert.True(observer.Docked);
            Assert.True(world.Undock(observer.Id));

            if (destroyed)
            {
                var attacker = world.AddPlayer("Raider");
                attacker.Position = new Vec3d(0, 3_000_000, 0);
                attacker.SetAngles(0, 0);
                hauler.Position = attacker.Position + new Vec3d(0, 0, -280);
                hauler.SetAngles(Math.PI, 0);
                hauler.Velocity = Vec3d.Zero;
                hauler.RouteActive = false;
                hauler.ApproachTargetId = 0;
                work.Goal = hauler.Position;
                work.Phase = NpcPhase.Waiting;
                work.Until = world.TimeSeconds + 10;
                hauler.Combat.Shield = hauler.Combat.Armor = 0;
                hauler.Combat.Hull = 1;
                world.ApplyInput(attacker.Id, InputState.None with { Fire = true });
                world.Tick(.05);
                Assert.True(hauler.Combat.Destroyed);
                Assert.Equal(quantity, world.CaptureSimulation().Economy!.CargoLost[commodity]);
            }
            else
            {
                hauler.Docked = true;
                hauler.DockingStationId = (uint)destination.Id;
                hauler.Position = destination.Position + ModelPose.StationDockingOffset;
                work.Until = 0;
                world.Tick(.05);
                Assert.Equal(0, work.Cargo);
                Assert.Equal(1, work.Deliveries);
            }
            return world.GetMarket(observer.Id)!.Entries.Single(row => row.CommodityId == commodity);
        }
    }
}
