using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record SavedPlayer(ShipState Ship, double CapturedAt, int StationId, int Credits, Dictionary<ushort, int> Cargo);

public sealed record SavedSimulation(
    ulong Tick, double TimeSeconds, uint NextPlayerId, ShipState[] Npcs,
    Dictionary<int, Dictionary<ushort, int>> StationStocks, Dictionary<int, int> Deposits,
    int[] Surveyed, ulong ActivitySequence, WorldActivity[] RecentActivities);

public sealed partial class GameWorld
{
    private readonly Queue<WorldActivity> _activityHistory = new();

    public SavedSimulation CaptureSimulation() => new(
        TickCount, TimeSeconds, _nextPlayerId, _npcs.ToArray(), _stationStocks,
        _deposits, SurveyedObjects.Order().ToArray(), _activitySequence, _activityHistory.ToArray());

    public SavedPlayer CapturePlayer(uint id)
    {
        var account = _accounts[id];
        return new(_players[id], TimeSeconds, account.StationId, account.Credits, account.Cargo);
    }

    public void RestoreSimulation(SavedSimulation state)
    {
        if (_players.Count != 0 || state.NextPlayerId < 10000 || !double.IsFinite(state.TimeSeconds) || state.TimeSeconds < 0 ||
            !state.Npcs.Select(npc => npc.Id).SequenceEqual(_npcs.Select(npc => npc.Id)) ||
            !state.StationStocks.Keys.Order().SequenceEqual(_stationStocks.Keys.Order()) ||
            state.StationStocks.Values.Any(stock => stock.Count != CommodityCatalog.All.Count ||
                stock.Any(item => CommodityCatalog.Find(item.Key) is null || item.Value < 0)) ||
            state.Npcs.Any(npc => npc.IsPlayer || npc.Work is null || !HullCatalog.Contains(npc.HullId) ||
                !npc.Position.IsFinite || !npc.Velocity.IsFinite))
            throw new InvalidOperationException("Saved simulation does not match this world.");

        TickCount = state.Tick;
        TimeSeconds = state.TimeSeconds;
        _nextPlayerId = state.NextPlayerId;
        _npcs.Clear();
        _npcs.AddRange(state.Npcs);
        _stationStocks.Clear();
        foreach (var (id, stock) in state.StationStocks) _stationStocks.Add(id, stock);
        _deposits.Clear();
        foreach (var (id, count) in state.Deposits) _deposits.Add(id, count);
        SurveyedObjects.Clear();
        SurveyedObjects.UnionWith(state.Surveyed);
        _activitySequence = state.ActivitySequence;
        _activities.Clear();
        _activityHistory.Clear();
        foreach (var activity in state.RecentActivities.TakeLast(128)) _activityHistory.Enqueue(activity);
    }

    public ShipState RestorePlayer(SavedPlayer saved)
    {
        var ship = saved.Ship;
        var account = new PlayerAccount { Credits = saved.Credits, StationId = saved.StationId };
        foreach (var item in saved.Cargo) account.Cargo.Add(item.Key, item.Value);
        var pausedFor = Math.Max(0, TimeSeconds - saved.CapturedAt);
        if (ship.InWarp) ship.WarpStartedAt += pausedFor;
        ship.ImpactAt += pausedFor;

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
