using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record SavedPlayer(ShipState Ship, double CapturedAt, int StationId, int Credits, Dictionary<ushort, int> Cargo,
    PlayerHangar? Hangar = null);

public sealed record SavedSimulation(
    ulong Tick, double TimeSeconds, uint NextPlayerId, ShipState[] Npcs,
    Dictionary<int, Dictionary<ushort, int>> StationStocks, Dictionary<int, int> Deposits,
    int[] Surveyed, ulong ActivitySequence, WorldActivity[] RecentActivities,
    ulong CombatSequence = 0, FlightProjectile[]? Projectiles = null, WorldWreck[]? Wrecks = null,
    SavedSecurity? Security = null, SavedEconomy? Economy = null);

public sealed record SavedSecurity(SecurityOffense[] Offenses, Dictionary<int, int> PirateSupplies,
    Dictionary<int, double> ReplacementOrders);

public sealed partial class GameWorld
{
    private readonly Queue<WorldActivity> _activityHistory = new();

    public SavedSimulation CaptureSimulation() => new(
        TickCount, TimeSeconds, _nextPlayerId, _npcs.ToArray(), _stationStocks,
        _deposits, SurveyedObjects.Order().ToArray(), _activitySequence, _activityHistory.ToArray(),
        _combatSequence, _projectiles.ToArray(), _wrecks.ToArray(),
        new SavedSecurity(_securityOffenses.Values.ToArray(), new(_raiderSupplies), new(_replacementOrders)),
        new SavedEconomy(_nextProduction, new(_stationLedgers), new(_cargoLost)));

    public SavedPlayer CapturePlayer(uint id)
    {
        var account = _accounts[id];
        return new(_players[id], TimeSeconds, account.StationId, account.Credits, account.Cargo, account.Hangar);
    }

    public void RestoreSimulation(SavedSimulation state)
    {
        var expectedNpcs = state.Security is null ? _npcs.Where(npc => npc.Id < FirstRaiderId) : _npcs;
        if (_players.Count != 0 || state.NextPlayerId < 10000 || !double.IsFinite(state.TimeSeconds) || state.TimeSeconds < 0 ||
            !state.Npcs.Select(npc => npc.Id).SequenceEqual(expectedNpcs.Select(npc => npc.Id)) ||
            !state.StationStocks.Keys.Order().SequenceEqual(_stationStocks.Keys.Order()) ||
            state.StationStocks.Values.Any(stock =>
                !CommodityCatalog.All.Where(good => state.Economy is not null || good.Id <= CommodityCatalog.Components)
                    .All(good => stock.ContainsKey(good.Id)) || !ValidUnits(stock)) ||
            state.Npcs.Any(npc => npc.IsPlayer || npc.Work is null || !HullCatalog.Contains(npc.HullId) ||
                !npc.Position.IsFinite || !npc.Velocity.IsFinite))
            throw new InvalidOperationException("Saved simulation does not match this world.");

        var newRaiders = state.Security is null ? _npcs.Where(npc => npc.Id >= FirstRaiderId).ToArray() : Array.Empty<ShipState>();
        if (state.Economy is { } economy)
        {
            if (!double.IsFinite(economy.NextProduction) || economy.NextProduction < 0 ||
                !economy.Stations.Keys.Order().SequenceEqual(_stationLedgers.Keys.Order()) || !ValidUnits(economy.CargoLost) ||
                economy.Stations.Values.Any(ledger => ledger.Credits < 0 || !ValidUnits(ledger.Produced) || !ValidUnits(ledger.Consumed)))
                throw new InvalidOperationException("Saved economy does not match this world.");
            _nextProduction = economy.NextProduction;
            _stationLedgers.Clear();
            foreach (var (id, ledger) in economy.Stations) _stationLedgers.Add(id, ledger);
            _cargoLost.Clear();
            foreach (var (commodity, amount) in economy.CargoLost) _cargoLost.Add(commodity, amount);
        }
        else _nextProduction = state.TimeSeconds + EconomyRules.ProductionInterval;
        if (state.Security is { } security)
        {
            if (!security.PirateSupplies.Keys.Order().SequenceEqual(_raiderSupplies.Keys.Order()) ||
                security.PirateSupplies.Values.Any(quantity => quantity is < 0 or > 150) ||
                security.ReplacementOrders.Any(order => order.Key == int.MinValue || Catalog.Find(Math.Abs(order.Key))?.Kind != BodyKind.Station ||
                    !double.IsFinite(order.Value) || order.Value < 0) ||
                security.Offenses.Any(offense => !offense.Position.IsFinite || !double.IsFinite(offense.ExpiresAt)))
                throw new InvalidOperationException("Saved security state does not match this world.");
            _raiderSupplies.Clear();
            foreach (var (id, quantity) in security.PirateSupplies) _raiderSupplies.Add(id, quantity);
            _replacementOrders.Clear();
            foreach (var (id, availableAt) in security.ReplacementOrders) _replacementOrders.Add(id, availableAt);
            _securityOffenses.Clear();
            foreach (var offense in security.Offenses) _securityOffenses.Add(offense.Suspect, offense);
        }
        TickCount = state.Tick;
        TimeSeconds = state.TimeSeconds;
        _nextPlayerId = state.NextPlayerId;
        _npcs.Clear();
        _npcs.AddRange(state.Npcs);
        _npcs.AddRange(newRaiders);
        _stationStocks.Clear();
        foreach (var (id, stock) in state.StationStocks)
        {
            var station = Catalog.Find(id)!;
            _stationStocks.Add(id, CommodityCatalog.All.ToDictionary(good => good.Id,
                good => stock.GetValueOrDefault(good.Id, EconomyRules.InitialStock(station.Purpose, good))));
        }
        _deposits.Clear();
        foreach (var (id, count) in state.Deposits) _deposits.Add(id, count);
        SurveyedObjects.Clear();
        SurveyedObjects.UnionWith(state.Surveyed);
        _activitySequence = state.ActivitySequence;
        _activities.Clear();
        _activityHistory.Clear();
        foreach (var activity in state.RecentActivities.TakeLast(128)) _activityHistory.Enqueue(activity);
        _combatSequence = state.CombatSequence;
        _combatEvents.Clear();
        _projectiles.Clear();
        if (state.Projectiles is { } projectiles) _projectiles.AddRange(projectiles);
        _wrecks.Clear();
        if (state.Wrecks is { } wrecks) _wrecks.AddRange(wrecks);
    }

    private static bool ValidUnits(Dictionary<ushort, int> units) =>
        units.All(item => CommodityCatalog.Find(item.Key) is not null && item.Value >= 0);

    public ShipState RestorePlayer(SavedPlayer saved)
    {
        var ship = saved.Ship;
        var account = new PlayerAccount { Credits = saved.Credits, StationId = saved.StationId, Hangar = saved.Hangar ?? new() };
        foreach (var item in saved.Cargo) account.Cargo.Add(item.Key, item.Value);
        var pausedFor = Math.Max(0, TimeSeconds - saved.CapturedAt);
        if (ship.InWarp) ship.WarpStartedAt += pausedFor;
        ship.ImpactAt += pausedFor;
        ship.Combat.TargetId = 0;
        ship.Combat.Order = IndifferentStars.Shared.Protocol.CombatOrder.None;

        // Offline ships do not reserve a berth. An occupied berth remains reserved
        // by its current ship; the captain resumes outside it before trading.
        if (ship.Docked || ship.DockingGranted)
        {
            var station = Catalog.Find((int)ship.DockingStationId)!;
            var occupied = _players.Values.Concat(_npcs).Any(other =>
                (other.Docked || other.DockingGranted) && other.DockingStationId == ship.DockingStationId);
            if (occupied)
            {
                var docked = ship.Docked;
                ship.Docked = false;
                ship.DockingGranted = false;
                ship.DockingStationId = 0;
                ship.ApproachTargetId = 0;
                if (docked) ship.Position = FindReturnPosition(station, ship);
            }
        }

        _players.Add(ship.Id, ship);
        _accounts.Add(ship.Id, account);
        _inputs.Add(ship.Id, new PlayerInputSlot());
        return ship;
    }

    private Vec3d FindReturnPosition(StaticBody station, ShipState ship)
    {
        var angle = ship.Id * 2.39996323;
        var point = station.Position + new Vec3d(
            Math.Cos(angle) * 900, station.Radius + 2500, Math.Sin(angle) * 900);
        for (var i = 0; i < 32; i++, point += Vec3d.Up * 1200)
            if (WarpSafe(ship, point, TimeSeconds)) return point;
        throw new InvalidOperationException("No safe return point near the occupied berth.");
    }
}
