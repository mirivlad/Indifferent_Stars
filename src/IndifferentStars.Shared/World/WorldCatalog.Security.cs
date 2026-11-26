using System;
using System.Linq;

namespace IndifferentStars.Shared.World;

public enum SecurityZone : byte { Trade, Industry, Corridor, Frontier, Pirate, Uninhabited }
public enum Faction : byte { Civilian, Security, Independent, Pirate }
public enum FactionStanding : sbyte { Hostile = -1, Neutral = 0, Friendly = 1 }

public readonly record struct RaiderTerritory(int StationId, Vec3d Hideout, Vec3d PatrolPoint);

public sealed partial class WorldCatalog
{
    private RaiderTerritory[]? _raiderTerritories;
    public RaiderTerritory[] RaiderTerritories => _raiderTerritories ??= Stations
        .Where(station => (station.Purpose is StationPurpose.Mining or StationPurpose.Freight) && station.Id != Station.Id)
        .OrderBy(station => station.Id).Take(2)
        .Select(station =>
        {
            var random = new SeedStream(Seed, station.Id, $"raider-territory-{SystemIndex}");
            var angle = random.Between(0, Math.Tau);
            var offset = new Vec3d(Math.Cos(angle), 0, Math.Sin(angle));
            return new RaiderTerritory(station.Id,
                station.Position + offset * 36_000 + Vec3d.Up * 26_000,
                station.Position + offset * 10_000 + Vec3d.Up * 21_000);
        }).ToArray();

    public SecurityZone SecurityAt(Vec3d position)
    {
        foreach (var station in Stations)
        {
            var distance = Vec3d.Distance(position, station.Position);
            if (station.Purpose == StationPurpose.Trade && distance <= 25_000) return SecurityZone.Trade;
            if (station.Purpose == StationPurpose.Mining && distance <= 16_000) return SecurityZone.Industry;
        }
        foreach (var territory in RaiderTerritories)
            if (Vec3d.Distance(position, territory.Hideout) <= 22_000) return SecurityZone.Pirate;
        if (Stations.Any(station => (station.Purpose is StationPurpose.Trade or StationPurpose.Freight) &&
            Vec3d.Distance(position, station.Position) <= 180_000)) return SecurityZone.Corridor;
        return Stations.Any(station => Vec3d.Distance(position, station.Position) <= 350_000)
            ? SecurityZone.Frontier : SecurityZone.Uninhabited;
    }

    public static FactionStanding DefaultStanding(Faction viewer, Faction other)
    {
        if (viewer == other) return FactionStanding.Friendly;
        if (viewer == Faction.Pirate || other == Faction.Pirate) return FactionStanding.Hostile;
        if ((viewer is Faction.Civilian or Faction.Security) && (other is Faction.Civilian or Faction.Security))
            return FactionStanding.Friendly;
        return FactionStanding.Neutral;
    }
}
