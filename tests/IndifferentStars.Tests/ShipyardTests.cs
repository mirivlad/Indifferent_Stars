using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class ShipyardTests
{
    [Fact]
    public void SwitchingLeavesTheOldHullAndItsConditionAtTheStation()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Pilot");
        Assert.Equal(ShipyardError.DockRequired, world.Shipyard(ship.Id, new(1, ShipyardAction.BuyHull, 10)).Error);
        Dock(world, ship);
        ship.Combat.Hull = 90;
        var capacity = ship.Hull.CargoCapacity;
        var bought = world.Shipyard(ship.Id, new(2, ShipyardAction.BuyHull, 10));
        Assert.Equal(ShipyardError.None, bought.Error);
        var original = bought.State!.ActiveHull;
        var next = bought.State.Vessels.Single(vessel => vessel.Serial != original);
        Assert.Equal(CommodityCatalog.StartingCredits - ShipyardCatalog.HullPrice(10), bought.State.Credits);
        Assert.Equal(10, next.HullId);
        Assert.Equal(capacity, world.GetMarket(ship.Id)!.CargoCapacity);

        var switched = world.Shipyard(ship.Id, new(3, ShipyardAction.ActivateHull, VesselId: next.Serial));

        Assert.Equal(ShipyardError.None, switched.Error);
        Assert.Equal((ushort)10, ship.HullId);
        Assert.Equal(next.Serial, switched.State!.ActiveHull);
        Assert.Equal(.9f, switched.State.Vessels.Single(vessel => vessel.Serial == original).Integrity);
        Assert.Equal(ship.Hull.CargoCapacity, world.GetMarket(ship.Id)!.CargoCapacity);
        Assert.Equal(10, world.BuildSnapshot().Single(entity => entity.Id == ship.Id).HullId);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(4, ShipyardAction.ActivateHull, VesselId: original)).Error);
        Assert.Equal(90, ship.Combat.Hull);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(5, ShipyardAction.SellHull, VesselId: next.Serial)).Error);
        Assert.Single(world.Shipyard(ship.Id, new(6, ShipyardAction.Inspect)).State!.Vessels);
    }

    [Fact]
    public void ModuleMovesBetweenStationStorageAndMountAndChangesAuthoritativeLimits()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Pilot");
        Dock(world, ship);
        var original = ship.Fit;
        var credits = world.GetMarket(ship.Id)!.Credits;
        var parts = world.StationStock(world.Catalog.Station.Id, CommodityCatalog.Equipment);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(1, ShipyardAction.BuyModule, 5)).Error);
        Assert.Equal(ShipyardError.Incompatible, world.Shipyard(ship.Id, new(2, ShipyardAction.FitModule, 5, 1, 0)).Error);
        var mounted = world.Shipyard(ship.Id, new(3, ShipyardAction.FitModule, 5, 1, 4));
        Assert.Equal(ShipyardError.None, mounted.Error);
        Assert.Empty(mounted.State!.Modules);
        Assert.Equal(original.CargoCapacity + 26, world.GetMarket(ship.Id)!.CargoCapacity);
        Assert.Equal(original.MassKg + 870, ship.MassKg);
        Assert.Equal(original.AvailableEnergy - 2, world.BuildCombatStatus(ship.Id)!.ReactorAvailable);
        Assert.Equal(parts - 1, world.StationStock(world.Catalog.Station.Id, CommodityCatalog.Equipment));
        Assert.Equal(credits - ShipyardCatalog.Module(5)!.Price, mounted.State.Credits);

        Assert.True(world.Trade(ship.Id, CommodityCatalog.Water, 43, true).Success);
        Assert.Equal(ShipyardError.Capacity, world.Shipyard(ship.Id, new(4, ShipyardAction.RemoveModule, VesselId: 1, Slot: 4)).Error);
        Assert.True(world.Trade(ship.Id, CommodityCatalog.Water, 5, false).Success);
        var removed = world.Shipyard(ship.Id, new(5, ShipyardAction.RemoveModule, VesselId: 1, Slot: 4));
        Assert.Equal(ShipyardError.None, removed.Error);
        Assert.Equal(1, Assert.Single(removed.State!.Modules).Quantity);
        Assert.Equal(original.MassKg, ship.MassKg);
        Assert.Equal(original.CargoCapacity, world.GetMarket(ship.Id)!.CargoCapacity);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(6, ShipyardAction.SellModule, 5)).Error);
        Assert.Equal(parts, world.StationStock(world.Catalog.Station.Id, CommodityCatalog.Equipment));
    }

    [Fact]
    public void RearmConsumesPartsAndCreditsAndRemovalDoesNotRestockWeapons()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Pilot");
        Dock(world, ship);
        var parts = world.StationStock(world.Catalog.Station.Id, CommodityCatalog.Components);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(1, ShipyardAction.BuyModule, 10)).Error);
        var slot = (byte)ship.Hull.EquipmentSlots.Length;
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(2, ShipyardAction.FitModule, 10, 1, slot)).Error);
        var weapon = Assert.Single(ship.Combat.Slots);
        Assert.Equal(WeaponKind.Cannon, weapon.Kind);
        Assert.Equal(0, weapon.Loaded);
        var quote = world.Shipyard(ship.Id, new(3, ShipyardAction.Inspect)).State!;
        var restocked = world.Shipyard(ship.Id, new(4, ShipyardAction.Rearm));
        Assert.Equal(ShipyardError.None, restocked.Error);
        Assert.Equal(quote.Credits - quote.RearmPrice, restocked.State!.Credits);
        Assert.True(world.StationStock(world.Catalog.Station.Id, CommodityCatalog.Components) < parts);
        Assert.Equal(WeaponCatalog.Get(WeaponKind.Cannon).Magazine, weapon.Loaded);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(5, ShipyardAction.RemoveModule, VesselId: 1, Slot: slot)).Error);
        Assert.Equal(ShipyardError.None, world.Shipyard(ship.Id, new(6, ShipyardAction.FitModule, 10, 1, slot)).Error);
        Assert.Equal(0, Assert.Single(ship.Combat.Slots).Reserve);
    }

    [Fact]
    public void FittingRejectsPowerAndMassOverflowAndPreservesClassSpecificSlots()
    {
        var swift = HullCatalog.Find("swift")!;
        Assert.False(ShipyardCatalog.ValidFit(swift, new[] { new FittedModule(0, 7) }));
        Assert.False(ShipyardCatalog.ValidFit(swift, new[] { new FittedModule(0, 1), new FittedModule(4, 6), new FittedModule(5, 6) }));
        var wren = HullCatalog.Find("wren")!;
        var heavy = new[] { new FittedModule(0, 1), new FittedModule(2, 4), new FittedModule(3, 3), new FittedModule(6, 9) };
        Assert.False(ShipyardCatalog.ValidFit(wren, heavy));
        Assert.True(ShipyardCatalog.ValidFit(wren, heavy.Append(new FittedModule(1, 2)).ToArray()));
        var mole = HullCatalog.Find("mole")!;
        Assert.True(ShipyardCatalog.ValidFit(mole, new[] { new FittedModule(6, 7) }));
        Assert.Equal(1.7, ShipyardCatalog.Calculate(mole, new[] { new FittedModule(6, 7) }).MiningFactor);
    }

    [Fact]
    public void ShipyardPacketsRoundTripAndRejectInvalidFittings()
    {
        var order = new ShipyardRequestMessage(8, ShipyardAction.FitModule, 3, 12, 3, Guid.NewGuid());
        Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.Encode(order), out var decoded, out var orderError), orderError);
        Assert.Equal(order, decoded);
        var fitting = new[] { new FittedModule(3, 3) };
        var value = new HangarVessel(12, 1, 3, .8f, fitting);
        var yard = new ShipyardState(3, 4000, 12, new[] { value },
            new[] { new HangarModule(3, 4, 1) }, new[] { new YardPrice(10, 1450, true) },
            new[] { new YardPrice(3, 980, true) }, 80, 100);
        var reply = new ShipyardReplyMessage(8, ShipyardAction.FitModule, ShipyardError.None, yard);
        var data = ProtocolCodec.Encode(reply);
        Assert.True(data.Length <= ProtocolConstants.MaxPacketSize);
        Assert.True(ProtocolCodec.TryDecode(data, out decoded, out var replyError), replyError);
        var roundTrip = Assert.IsType<ShipyardReplyMessage>(decoded).State!;
        Assert.Equal(yard.Modules, roundTrip.Modules);
        Assert.Equal(yard.HullOffers, roundTrip.HullOffers);
        Assert.Equal(fitting, Assert.Single(roundTrip.Vessels).Modules);
        Assert.Equal(yard.ActiveHull, roundTrip.ActiveHull);
        var invalid = reply with { State = yard with { Vessels = new[] { value with { Modules = new[] { new FittedModule(0, 3) } } } } };
        Assert.False(ProtocolCodec.TryDecode(ProtocolCodec.Encode(invalid), out _, out _));
    }

    [Fact]
    public void RecoveryReplacesTheDestroyedActiveHullAndKeepsStoredAssets()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Pilot");
        Dock(world, ship);
        var purchased = world.Shipyard(ship.Id, new(1, ShipyardAction.BuyHull, 10)).State!;
        Assert.Equal(2, purchased.Vessels.Length);
        ship.Docked = false;
        ship.Combat.Destroyed = true;
        ship.Combat.Hull = 0;

        Assert.True(world.ApplyCombatCommand(ship.Id, new(2, CombatCommand.Recover)));

        var hangar = world.CapturePlayer(ship.Id).Hangar!;
        Assert.NotEqual(purchased.ActiveHull, hangar.Active);
        Assert.Equal(10, Assert.Single(hangar.Vessels).HullId);
        Assert.Equal(HullCatalog.Courier, ship.HullId);
    }

    private static void Dock(GameWorld world, ShipState ship)
    {
        Assert.True(world.RequestDocking(ship.Id, (uint)world.Catalog.Station.Id));
        ship.Position = ship.DockingPoint;
        world.Tick(.05);
        Assert.True(ship.Docked);
    }
}
