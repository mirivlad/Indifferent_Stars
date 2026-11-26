using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record StoredVessel(uint Serial, ushort HullId, int StationId, FittedModule[] Modules, ShipCombat Combat);

public sealed class PlayerHangar
{
    public uint Active { get; set; } = 1;
    public uint NextSerial { get; set; } = 2;
    public List<StoredVessel> Vessels { get; set; } = new();
    public List<HangarModule> Modules { get; set; } = new();
}

public sealed partial class GameWorld
{
    public bool ValidHangar(SavedPlayer saved)
    {
        if (!ShipyardCatalog.ValidFit(saved.Ship.Hull, saved.Ship.FittedModules) || !ValidUnits(saved.Cargo) ||
            saved.Cargo.Values.Sum() > saved.Ship.Fit.CargoCapacity) return false;
        if (saved.Hangar is not { } hangar) return saved.Ship.FittedModules.Length == 0;
        if (hangar.Active == 0 || hangar.NextSerial <= hangar.Active || hangar.Vessels.Count >= ShipyardState.MaxVessels ||
            hangar.Modules.Count > ShipyardState.MaxModuleRows ||
            hangar.Vessels.Select(vessel => vessel.Serial).Distinct().Count() != hangar.Vessels.Count ||
            hangar.Modules.Select(module => (module.StationId, module.ModuleId)).Distinct().Count() != hangar.Modules.Count)
            return false;
        return hangar.Vessels.All(vessel => vessel.Serial > 0 && vessel.Serial < hangar.NextSerial &&
                   vessel.Serial != hangar.Active && Catalog.Find(vessel.StationId)?.Kind == BodyKind.Station &&
                   vessel.HullId != 0 && HullCatalog.Contains(vessel.HullId) && ValidStoredFit(vessel)) &&
               hangar.Modules.All(row => Catalog.Find((int)row.StationId)?.Kind == BodyKind.Station &&
                   ShipyardCatalog.Module(row.ModuleId) is not null && row.Quantity is > 0 and <= 99);

        static bool ValidStoredFit(StoredVessel vessel)
        {
            var hull = HullCatalog.Get(vessel.HullId);
            if (!ShipyardCatalog.ValidFit(hull, vessel.Modules) || vessel.Combat.Destroyed ||
                vessel.Combat.Hull <= 0 || vessel.Combat.Hull > hull.HullIntegrity)
                return false;
            return ShipyardCatalog.Calculate(hull, vessel.Modules).Weapons.SequenceEqual(vessel.Combat.Slots.Select(slot => slot.Kind));
        }
    }

    public ShipyardReplyMessage Shipyard(uint id, ShipyardRequestMessage request)
    {
        if (GetPlayer(id) is not { Docked: true } ship || ship.Combat.Destroyed ||
            Catalog.Find((int)ship.DockingStationId) is not { Kind: BodyKind.Station } station)
            return new(request.RequestId, request.Action, ShipyardError.DockRequired, null);
        var account = _accounts[id];
        var result = request.Action switch
        {
            ShipyardAction.Inspect => ShipyardError.None,
            ShipyardAction.BuyHull => BuyVessel(account, station, request.CatalogId),
            ShipyardAction.SellHull => SellVessel(account, station, request.VesselId),
            ShipyardAction.ActivateHull => ActivateVessel(ship, account, station, request.VesselId),
            ShipyardAction.BuyModule => BuyModule(account, station, request.CatalogId),
            ShipyardAction.SellModule => SellModule(account, station, request.CatalogId),
            ShipyardAction.FitModule or ShipyardAction.RemoveModule => Refit(ship, account, station, request),
            ShipyardAction.Repair => Maintain(ship, account, station, repair: true),
            ShipyardAction.Rearm => Maintain(ship, account, station, repair: false),
            _ => ShipyardError.Unavailable,
        };
        return new(request.RequestId, request.Action, result, BuildShipyard(ship, account, station));
    }

    private ShipyardState BuildShipyard(ShipState ship, PlayerAccount account, StaticBody station)
    {
        var active = new HangarVessel(account.Hangar.Active, ship.EffectiveHullId, ship.DockingStationId,
            (float)(ship.Combat.Hull / ship.Hull.HullIntegrity), ship.FittedModules);
        var vessels = account.Hangar.Vessels.Select(vessel => new HangarVessel(vessel.Serial, vessel.HullId,
            (uint)vessel.StationId, (float)(vessel.Combat.Hull / HullCatalog.Get(vessel.HullId).HullIntegrity), vessel.Modules,
            VesselResale(vessel)));
        var hulls = HullCatalog.All.Where(hull => ShipyardCatalog.SellsHull(station.Purpose, hull.Id))
            .Select(hull => new YardPrice(hull.Id, ShipyardCatalog.HullPrice(hull.Id),
                StationStock(station.Id, CommodityCatalog.Components) >= HullParts(hull.Id)));
        var modules = ShipyardCatalog.Modules.Where(module => ShipyardCatalog.SellsModule(station.Purpose, module))
            .Select(module => new YardPrice(module.Id, module.Price, StationStock(station.Id, CommodityCatalog.Equipment) > 0));
        return new((uint)station.Id, account.Credits, account.Hangar.Active, vessels.Prepend(active).ToArray(),
            account.Hangar.Modules.ToArray(), hulls.ToArray(), modules.ToArray(), RepairQuote(ship).Credits, RearmQuote(ship).Credits);
    }

    private ShipyardError BuyVessel(PlayerAccount account, StaticBody station, ushort hullId)
    {
        if (!ShipyardCatalog.SellsHull(station.Purpose, hullId)) return ShipyardError.Unavailable;
        if (account.Hangar.Vessels.Count + 1 >= ShipyardState.MaxVessels || account.Hangar.NextSerial == uint.MaxValue)
            return ShipyardError.Capacity;
        var price = ShipyardCatalog.HullPrice(hullId);
        var paid = PayYard(account, station.Id, price, CommodityCatalog.Components, HullParts(hullId));
        if (paid != ShipyardError.None) return paid;
        account.Hangar.Vessels.Add(new StoredVessel(account.Hangar.NextSerial++, hullId, station.Id,
            Array.Empty<FittedModule>(), ShipCombat.Create(HullCatalog.Get(hullId))));
        return ShipyardError.None;
    }

    private ShipyardError SellVessel(PlayerAccount account, StaticBody station, uint serial)
    {
        var vessel = account.Hangar.Vessels.Find(item => item.Serial == serial);
        if (vessel is null) return ShipyardError.Unavailable;
        if (vessel.StationId != station.Id) return ShipyardError.WrongStation;
        var credits = VesselResale(vessel);
        var ledger = _stationLedgers[station.Id];
        if (ledger.Credits < credits) return ShipyardError.Credits;
        account.Credits += credits;
        ledger.Credits -= credits;
        account.Hangar.Vessels.Remove(vessel);
        StoreReclaimed(station.Id, CommodityCatalog.Components, Math.Max(1, HullParts(vessel.HullId) / 2));
        return ShipyardError.None;
    }

    private static ShipyardError ActivateVessel(ShipState ship, PlayerAccount account, StaticBody station, uint serial)
    {
        var next = account.Hangar.Vessels.Find(vessel => vessel.Serial == serial);
        if (next is null) return ShipyardError.Unavailable;
        if (next.StationId != station.Id) return ShipyardError.WrongStation;
        var fit = ShipyardCatalog.Calculate(HullCatalog.Get(next.HullId), next.Modules);
        if (CargoUsed(account) > fit.CargoCapacity) return ShipyardError.Capacity;
        ship.Combat.TargetId = 0;
        ship.Combat.Order = CombatOrder.None;
        account.Hangar.Vessels.Remove(next);
        account.Hangar.Vessels.Add(new StoredVessel(account.Hangar.Active, ship.EffectiveHullId,
            station.Id, ship.FittedModules, ship.Combat));
        account.Hangar.Active = next.Serial;
        ship.HullId = next.HullId;
        ship.FittedModules = next.Modules;
        ship.Combat = next.Combat;
        ship.Impact = null;
        return ShipyardError.None;
    }

    private ShipyardError BuyModule(PlayerAccount account, StaticBody station, ushort id)
    {
        if (ShipyardCatalog.Module(id) is not { } module || !ShipyardCatalog.SellsModule(station.Purpose, module))
            return ShipyardError.Unavailable;
        var index = FindModule(account, station.Id, id);
        if (!CanStoreModule(account, index)) return ShipyardError.Capacity;
        var paid = PayYard(account, station.Id, module.Price, CommodityCatalog.Equipment, 1);
        if (paid != ShipyardError.None) return paid;
        AddModule(account, (uint)station.Id, id, index);
        return ShipyardError.None;
    }

    private ShipyardError SellModule(PlayerAccount account, StaticBody station, ushort id)
    {
        if (ShipyardCatalog.Module(id) is not { } module) return ShipyardError.Unavailable;
        var index = FindModule(account, station.Id, id);
        if (index < 0) return ShipyardError.WrongStation;
        var value = module.Price / 2;
        var ledger = _stationLedgers[station.Id];
        if (ledger.Credits < value) return ShipyardError.Credits;
        RemoveModule(account, index);
        account.Credits += value;
        ledger.Credits -= value;
        StoreReclaimed(station.Id, CommodityCatalog.Equipment, 1);
        return ShipyardError.None;
    }

    private static ShipyardError Refit(ShipState ship, PlayerAccount account, StaticBody station, ShipyardRequestMessage request)
    {
        if (request.VesselId != account.Hangar.Active) return ShipyardError.Stale;
        var installed = ship.FittedModules.SingleOrDefault(fit => fit.Slot == request.Slot);
        var add = request.Action == ShipyardAction.FitModule;
        if (add && installed.ModuleId != 0 || !add && installed.ModuleId == 0) return ShipyardError.Unavailable;
        var id = add ? request.CatalogId : installed.ModuleId;
        if (ShipyardCatalog.Module(id) is not { } module || ShipyardCatalog.SlotCategory(ship.Hull, request.Slot) != module.Category)
            return ShipyardError.Incompatible;
        var index = FindModule(account, station.Id, id);
        if (add && index < 0) return ShipyardError.WrongStation;
        if (!add && !CanStoreModule(account, index)) return ShipyardError.Capacity;
        var modules = add ? ship.FittedModules.Append(new FittedModule(request.Slot, id)).OrderBy(fit => fit.Slot).ToArray()
            : ship.FittedModules.Where(fit => fit.Slot != request.Slot).ToArray();
        if (!ShipyardCatalog.ValidFit(ship.Hull, modules)) return ShipyardError.Limit;
        if (CargoUsed(account) > ShipyardCatalog.Calculate(ship.Hull, modules).CargoCapacity) return ShipyardError.Capacity;
        if (add) RemoveModule(account, index);
        else AddModule(account, (uint)station.Id, id, index);

        var previous = ship.Fit;
        var state = ship.Combat;
        ship.FittedModules = modules;
        var fit = ship.Fit;
        state.Shield = Math.Min(fit.Shield, state.Shield / previous.Shield * fit.Shield);
        state.Armor = Math.Min(fit.Armor, state.Armor / previous.Armor * fit.Armor);
        state.Energy = Math.Min(fit.AvailableEnergy, state.Energy);
        for (var i = 0; i < fit.Weapons.Length; i++)
        {
            var weapon = fit.Weapons[i];
            if (state.Slots[i].Kind != weapon)
                state.Slots[i] = new EquippedWeapon { Kind = weapon };
        }
        return ShipyardError.None;
    }

    private ShipyardError Maintain(ShipState ship, PlayerAccount account, StaticBody station, bool repair)
    {
        var quote = repair ? RepairQuote(ship) : RearmQuote(ship);
        if (repair)
        {
            if (quote.Credits > account.Credits) return ShipyardError.Credits;
            return RepairPlayer(ship) ? ShipyardError.None : ShipyardError.Parts;
        }
        var paid = PayYard(account, station.Id, quote.Credits, CommodityCatalog.Components, quote.Parts);
        if (paid != ShipyardError.None) return paid;
        foreach (var slot in ship.Combat.Slots)
        {
            var weapon = WeaponCatalog.Get(slot.Kind);
            slot.Loaded = weapon.Magazine;
            slot.Reserve = weapon.SpareRounds;
            slot.ReloadAt = 0;
        }
        return ShipyardError.None;
    }

    private ShipyardError PayYard(PlayerAccount account, int station, int credits, ushort supply, int amount)
    {
        if (account.Credits < credits) return ShipyardError.Credits;
        if (StationStock(station, supply) < amount) return ShipyardError.Parts;
        account.Credits -= credits;
        _stationLedgers[station].Credits += credits;
        Consume(station, supply, amount);
        return ShipyardError.None;
    }

    private void StoreReclaimed(int station, ushort commodity, int amount)
    {
        var stock = _stationStocks[station];
        var capacity = EconomyRules.Capacity(CommodityCatalog.Find(commodity)!);
        stock[commodity] += Math.Max(0, Math.Min(amount, capacity - stock[commodity]));
    }

    private static (int Credits, int Parts) RepairQuote(ShipState ship)
    {
        var damage = Math.Max(0, ship.Hull.HullIntegrity - ship.Combat.Hull + ship.Fit.Armor - ship.Combat.Armor);
        return ((int)Math.Ceiling(damage * 3), (int)Math.Ceiling(damage / 18));
    }

    private static (int Credits, int Parts) RearmQuote(ShipState ship)
    {
        var shortage = 0;
        foreach (var slot in ship.Combat.Slots)
        {
            var weapon = WeaponCatalog.Get(slot.Kind);
            shortage += Math.Max(0, weapon.Magazine + weapon.SpareRounds - slot.Loaded - slot.Reserve) *
                (slot.Kind == WeaponKind.Missile ? 6 : 1);
        }
        return (shortage * 4, (shortage + 23) / 24);
    }

    private static int HullParts(ushort hullId) => 1 + ShipyardCatalog.HullPrice(hullId) / 2500;

    private static int VesselResale(StoredVessel vessel)
    {
        var integrity = vessel.Combat.Hull / HullCatalog.Get(vessel.HullId).HullIntegrity;
        return (int)(ShipyardCatalog.HullPrice(vessel.HullId) * integrity * .55) +
            vessel.Modules.Sum(fit => ShipyardCatalog.Module(fit.ModuleId)!.Price / 3);
    }

    private static int FindModule(PlayerAccount account, int stationId, ushort id) => account.Hangar.Modules.FindIndex(
        row => row.StationId == stationId && row.ModuleId == id);

    private static bool CanStoreModule(PlayerAccount account, int index) => index < 0
        ? account.Hangar.Modules.Count < ShipyardState.MaxModuleRows
        : account.Hangar.Modules[index].Quantity < 99;

    private static void AddModule(PlayerAccount account, uint station, ushort id, int index)
    {
        if (index < 0) account.Hangar.Modules.Add(new HangarModule(station, id, 1));
        else
        {
            var row = account.Hangar.Modules[index];
            account.Hangar.Modules[index] = row with { Quantity = (ushort)(row.Quantity + 1) };
        }
    }

    private static void RemoveModule(PlayerAccount account, int index)
    {
        var row = account.Hangar.Modules[index];
        if (row.Quantity == 1) account.Hangar.Modules.RemoveAt(index);
        else account.Hangar.Modules[index] = row with { Quantity = (ushort)(row.Quantity - 1) };
    }
}
