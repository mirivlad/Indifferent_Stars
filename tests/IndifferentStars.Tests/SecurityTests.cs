using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void TerritoryAndAffiliationsAreStableWithoutPlayers()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var first = new GameWorld(catalog);
        var second = new GameWorld(WorldCatalog.Generate(20261101));
        Assert.Empty(first.Players);
        Assert.Equal(first.Npcs.Select(ship => (ship.Id, ship.Name, ship.Position)),
            second.Npcs.Select(ship => (ship.Id, ship.Name, ship.Position)));
        Assert.Equal(SecurityZone.Trade, catalog.SecurityAt(catalog.Station.Position));
        Assert.Equal(SecurityZone.Industry, catalog.SecurityAt(catalog.Stations.First(station => station.Purpose == StationPurpose.Mining).Position));
        var territory = catalog.RaiderTerritories[0];
        Assert.Equal(SecurityZone.Pirate, catalog.SecurityAt(territory.Hideout));
        Assert.Equal(SecurityZone.Uninhabited, catalog.SecurityAt(new Vec3d(1e13, 0, 0)));
        var raiders = first.Npcs.Where(ship => GameWorld.Affiliation(ship) == Faction.Pirate).ToArray();
        Assert.Equal(catalog.RaiderTerritories.Length * 2, raiders.Length);
        Assert.All(raiders, ship => Assert.False(ship.TransponderEnabled));
        var civilian = first.Npcs.First(ship => GameWorld.Affiliation(ship) == Faction.Civilian);
        Assert.Equal(FactionStanding.Hostile, first.Standing(civilian, raiders[0]));
        var patrol = first.Npcs.First(ship => GameWorld.Affiliation(ship) == Faction.Security);
        Assert.Equal(FactionStanding.Friendly, first.Standing(civilian, patrol));
    }

    [Fact]
    public void RaiderEngagesCargoNpcAndPatrolRespondsToRealFire()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var territory = catalog.RaiderTerritories[0];
        var raider = world.Npcs.First(ship => ship.Work?.Occupation == NpcOccupation.Raider);
        var target = world.Npcs.First(ship => ship.Work?.Occupation == NpcOccupation.Courier && ship.Hull.CargoCapacity >= 40);
        var patrol = world.Npcs.First(ship => ship.Work?.Occupation == NpcOccupation.Patrol);
        var otherRaider = world.Npcs.First(ship => ship.Id != raider.Id && ship.Work?.Occupation == NpcOccupation.Raider);
        otherRaider.Position = territory.PatrolPoint + Vec3d.Up * 100_000;
        raider.Position = territory.PatrolPoint;
        raider.SetAngles(0, 0);
        target.Position = raider.Position + new Vec3d(0, 0, -520);
        target.SetAngles(Math.PI, 0);
        target.Work!.Goal = target.Position;
        target.Work.Phase = NpcPhase.Waiting;
        target.Work.Until = 60;
        target.Work.Cargo = 3;
        target.Work.CommodityId = 1;
        patrol.Position = raider.Position + new Vec3d(0, 3500, 0);
        var originalPatrolPosition = patrol.Position;
        var initialShield = target.Combat.Shield;

        for (var step = 0; step < 110; step++) world.Tick(.05);

        Assert.Empty(world.Players);
        Assert.True(target.Combat.Shield < initialShield);
        Assert.Contains(world.Activities, item => item.Kind == "distress" && item.SourceId == target.Id);
        Assert.Contains(world.Activities, item => item.Kind == "pursuit" && item.SourceId == patrol.Id);
        Assert.InRange(Vec3d.Distance(originalPatrolPosition, patrol.Position), 1, 4500);
        Assert.True(target.Work.Security.RouteAvoidanceUntil > world.TimeSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PiratesCanAttackThePlayerAndAvoidPatrolledTradeSpace(bool patrolledTradeSpace)
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var target = world.AddPlayer("Cargo pilot", HullCatalog.Find("tern")!.Id);
        var raider = world.Npcs.First(ship => ship.Work?.Occupation == NpcOccupation.Raider);
        raider.Position = patrolledTradeSpace ? catalog.Station.Position + Vec3d.Up * 6000 : catalog.RaiderTerritories[0].PatrolPoint;
        raider.SetAngles(0, 0);
        target.Position = raider.Position + new Vec3d(0, 0, -410);
        target.SetAngles(Math.PI, 0);
        var initialShield = target.Combat.Shield;

        for (var step = 0; step < 30; step++) world.Tick(.05);

        if (patrolledTradeSpace) Assert.Equal(initialShield, target.Combat.Shield);
        else Assert.True(target.Combat.Shield < initialShield);
    }

    [Fact]
    public void ReplacementWaitsForLaunchAndUsesStationComponents()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var civilian = world.Npcs.First(ship => GameWorld.Affiliation(ship) == Faction.Civilian);
        var prior = civilian.Position;
        var count = world.Npcs.Count;
        var stock = world.StationStock(civilian.Work!.HomeStationId, 6);
        civilian.Combat.Destroyed = true;
        civilian.Work.Security.ReplacementAt = world.TimeSeconds + 1.5;
        for (var i = 0; i < 20; i++) world.Tick(.05);
        Assert.True(civilian.Combat.Destroyed);
        for (var i = 0; i < 40; i++) world.Tick(.05);
        Assert.False(civilian.Combat.Destroyed);
        Assert.Equal(count, world.Npcs.Count);
        Assert.Equal(stock - 8, world.StationStock(civilian.Work.HomeStationId, 6));
        Assert.True(Vec3d.Distance(civilian.Position, prior) > 200);
        Assert.Equal(1, civilian.Work.Security.Replacements);
    }

    [Fact]
    public void LegacySimulationKeepsWorkersAndAddsSeededRaiders()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var previous = new GameWorld(catalog);
        var state = previous.CaptureSimulation();
        var legacy = state with
        {
            Npcs = state.Npcs.Where(ship => ship.Work?.Occupation != NpcOccupation.Raider).ToArray(),
            Security = null,
        };
        var restored = new GameWorld(catalog);
        restored.RestoreSimulation(legacy);
        Assert.Equal(state.Npcs.Select(ship => ship.Id), restored.Npcs.Select(ship => ship.Id));
        Assert.Equal(state.Npcs.Select(ship => ship.Position), restored.Npcs.Select(ship => ship.Position));
    }

    [Fact]
    public void RaiderTakesFiniteWreckCargoAndServicesAtTheHideout()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var seedWorld = new GameWorld(catalog);
        var territory = catalog.RaiderTerritories[0];
        var raider = seedWorld.Npcs.First(ship => ship.Work?.Occupation == NpcOccupation.Raider);
        raider.Position = territory.Hideout + Vec3d.Up * 15;
        raider.Work!.Goal = raider.Position;
        raider.Combat.Armor *= .2;
        var armor = raider.Combat.Armor;
        var wreck = new WorldWreck(1, "Cargo vessel", raider.Position, 300, new() { [5] = 4 });
        var snapshot = seedWorld.CaptureSimulation() with { CombatSequence = 1, Wrecks = new[] { wreck } };
        var world = new GameWorld(catalog);
        world.RestoreSimulation(snapshot);

        world.Tick(.05);

        Assert.Empty(wreck.Cargo);
        Assert.Equal(4, raider.Work.Security.Loot[(ushort)5]);
        Assert.True(raider.Work.Security.Returning);
        world.Tick(.05);
        Assert.Empty(raider.Work.Security.Loot);
        Assert.True(raider.Combat.Armor > armor);
        Assert.Equal(66, world.CaptureSimulation().Security!.PirateSupplies[territory.StationId]);
    }

    [Fact]
    public void ResearchHaulPaysAndTheExposedMinePaysMoreForSupplies()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var trader = world.AddPlayer("Trader");
        trader.Docked = true;
        Assert.True(world.Trade(trader.Id, 5, 2, true).Success);
        Assert.True(world.Trade(trader.Id, 3, 2, true).Success);
        var beforeSales = world.GetMarket(trader.Id)!.Credits;
        DockAt(StationPurpose.Research);
        var safeBid = world.GetMarket(trader.Id)!.Entries.Single(entry => entry.CommodityId == 5).SellPrice;
        var safe = world.Trade(trader.Id, 5, 2, false);
        Assert.True(safe.Success);
        Assert.Equal(54, safeBid);
        Assert.Equal(beforeSales + safeBid * 2, safe.State!.Credits);
        DockAt(StationPurpose.Mining);
        var exposedBid = world.GetMarket(trader.Id)!.Entries.Single(entry => entry.CommodityId == 3).SellPrice;
        var exposed = world.Trade(trader.Id, 3, 2, false);
        Assert.True(exposed.Success);
        Assert.Equal(81, exposedBid);
        Assert.Equal(CommodityCatalog.StartingCredits + 12 + 32, exposed.State!.Credits);

        void DockAt(StationPurpose purpose)
        {
            Assert.True(world.Undock(trader.Id));
            var station = catalog.Stations.First(body => body.Purpose == purpose);
            trader.Position = station.Position + ModelPose.StationDockingOffset;
            Assert.True(world.RequestDocking(trader.Id, (uint)station.Id));
            world.Tick(.05);
            Assert.True(trader.Docked);
        }
    }
}
