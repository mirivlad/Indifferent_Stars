using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;
public sealed partial class GameWorld
{
    private readonly Dictionary<uint, Dictionary<uint, SensorContact>> _sensorHistory = new();

    public ContactSource? Detect(ShipState viewer, ShipState target)
    {
        if (viewer.Id == target.Id || target.InWarp) return null;
        if (target.TransponderEnabled) return ContactSource.Transponder;
        var distance = Vec3d.Distance(viewer.Position, target.Position);
        if (distance <= WorldCatalog.TacticalRange) return ContactSource.Tactical;
        if (distance <= WorldCatalog.DistantSensorRange && (target.Cruising || target.Boosting || HasClusterSignal(target)))
            return ContactSource.DistantSignal;
        return null;
    }

    private bool HasClusterSignal(ShipState target)
    {
        var members=0;
        foreach(var ship in _players.Values.Concat(_npcs))
            if(!ship.InWarp && Vec3d.Distance(ship.Position,target.Position)<=WorldCatalog.ClusterSignalRadius &&
                ++members>=WorldCatalog.ClusterSignalMembers)return true;
        return false;
    }

    // Navigation resolves the current server position only when accurate information is available.
    private bool CanLocate(ShipState viewer, ShipState target) =>
        Detect(viewer, target) is ContactSource.Transponder or ContactSource.Tactical;

    public SensorContact[] BuildSensorContacts(uint viewerId)
    {
        var viewer = GetPlayer(viewerId);
        if (viewer is null) return Array.Empty<SensorContact>();
        if (!_sensorHistory.TryGetValue(viewerId, out var history))
            _sensorHistory[viewerId] = history = new();
        var live = new HashSet<uint>();
        var now = (ulong)Math.Round(TimeSeconds * 1000);
        foreach (var target in _players.Values.Concat(_npcs))
        {
            if (Detect(viewer, target) is not { } source) continue;
            live.Add(target.Id);
            var anonymous = source == ContactSource.DistantSignal;
            var position = anonymous ? new Vec3d(
                Math.Round(target.Position.X / 100_000) * 100_000,
                Math.Round(target.Position.Y / 100_000) * 100_000,
                Math.Round(target.Position.Z / 100_000) * 100_000) : target.Position;
            history[target.Id] = new(target.Id, anonymous ? "?" : target.Name, position,
                anonymous ? Vec3d.Zero : target.Velocity, source, now, target.TransponderEnabled,
                target.TransponderEnabled ? (uint)(target.Work?.TargetId ?? 0) : 0,
                anonymous ? 0 : target.IsPlayer ? EntityKind.Player : EntityKind.Npc);
        }
        foreach (var (id, contact) in history.ToArray())
        {
            if (live.Contains(id)) continue;
            var age = now - contact.ObservedAtMs;
            if (age > 60_000) history.Remove(id);
            else history[id] = contact with { Source = age <= 10_000 ? ContactSource.LastKnown : ContactSource.Lost };
        }
        return history.Values.OrderBy(c => c.Id).ToArray();
    }
}
