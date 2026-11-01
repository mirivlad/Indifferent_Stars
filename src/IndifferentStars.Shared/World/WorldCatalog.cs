using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IndifferentStars.Shared.World;

public enum BodyKind : byte
{
    Star = 1,
    Planet = 2,
    Station = 3,
    Asteroid = 4,
}

/// <summary>
/// A static (non-simulated) world object. The catalog is generated deterministically
/// from a seed by both the server and the client, so it never has to be sent in full.
/// </summary>
public sealed class StaticBody
{
    public required int Id { get; init; }
    public required BodyKind Kind { get; init; }
    public required string Name { get; init; }
    public required Vec3d Position { get; init; }
    public required double Radius { get; init; }
    public float Rotation { get; init; }
    public int Variant { get; init; }
}

/// <summary>
/// Static contents of the single M0 star system. Positions are in metres in one sector
/// (system-local coordinates), Y is the third axis (up relative to the ecliptic).
/// </summary>
public sealed class WorldCatalog
{
    public const double PlayRadius = 26000.0;
    public const double WarpRange = 21000.0;
    public const double ScanRange = 4200.0;
    public const double MaxSpeed = 260.0;
    public const double MaxBoostSpeed = 720.0;
    public const float StartFuel = 100f;

    public required int Seed { get; init; }
    public required string SystemName { get; init; }
    public required StaticBody Star { get; init; }
    public required StaticBody Planet { get; init; }
    public required StaticBody Station { get; init; }
    public required IReadOnlyList<StaticBody> Asteroids { get; init; }

    private IReadOnlyDictionary<int, StaticBody>? _byId;

    public IReadOnlyList<StaticBody> AllBodies
    {
        get
        {
            var all = new List<StaticBody>(Asteroids.Count + 3) { Star, Planet, Station };
            all.AddRange(Asteroids);
            return all;
        }
    }

    public StaticBody? Find(int id)
    {
        _byId ??= AllBodies.ToDictionary(b => b.Id);
        return _byId.TryGetValue(id, out var body) ? body : null;
    }

    public static WorldCatalog Generate(int seed)
    {
        var random = new Random(seed);

        var star = new StaticBody
        {
            Id = 1,
            Kind = BodyKind.Star,
            Name = "Vesper",
            Position = Vec3d.Zero,
            Radius = 2200,
            Rotation = 0,
        };

        var planet = new StaticBody
        {
            Id = 2,
            Kind = BodyKind.Planet,
            Name = "Aster Reach",
            Position = new Vec3d(14800, -1100, -10200),
            Radius = 2850,
            Rotation = (float)(random.NextDouble() * Math.PI * 2),
        };

        var station = new StaticBody
        {
            Id = 3,
            Kind = BodyKind.Station,
            Name = "Kestrel Reach Station",
            Position = new Vec3d(6200, 350, -4300),
            Radius = 160,
            Rotation = -0.55f,
        };

        var asteroids = new List<StaticBody>(240);
        const int asteroidCount = 260;
        for (var i = 0; i < asteroidCount; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2;
            var radius = 8200 + random.NextDouble() * 3600;
            var y = (random.NextDouble() - 0.5) * 850;
            var size = 9 + Math.Pow(random.NextDouble(), 2.2) * 70;
            asteroids.Add(new StaticBody
            {
                Id = 1000 + i,
                Kind = BodyKind.Asteroid,
                Name = $"Asteroid KV-{1000 + i}",
                Position = new Vec3d(Math.Cos(angle) * radius, y, Math.Sin(angle) * radius),
                Radius = size,
                Rotation = (float)(random.NextDouble() * Math.PI * 2),
                Variant = random.Next(0, 3),
            });
        }

        return new WorldCatalog
        {
            Seed = seed,
            SystemName = "Vesper — Kestrel Reach",
            Star = star,
            Planet = planet,
            Station = station,
            Asteroids = new ReadOnlyCollection<StaticBody>(asteroids),
        };
    }
}