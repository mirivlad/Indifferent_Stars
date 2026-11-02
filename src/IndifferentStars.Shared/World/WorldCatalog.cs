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
/// A static object inside the local sector (metres relative to the sector origin).
/// Only nearby, directly simulated objects live here; see <see cref="CelestialBody"/>
/// for stars and planets at astronomical distances.
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
/// Static contents of the single M0 star system. The catalog is deterministic from a
/// seed and is shared by server and client.
///
/// Scale contract:
///  - <see cref="CelestialBody.SystemPosition"/> and radius are real system coordinates
///    in metres (star at the barycentre, planet about one AU away).
///  - <see cref="Station"/> and <see cref="Asteroids"/> use metres relative to
///    <see cref="LocalFrameOrigin"/>, a small playable sector near the station.
///  - The client never renders system coordinates directly; it maps distant bodies
///    into a display frame that preserves their angular size from the sector origin.
/// </summary>
public enum SectorKind : byte
{
    Habitated = 1,
    Planetary = 2,
    Asteroid = 3,
    DeepSpace = 4,
    Unknown = 5,
}

/// <summary>
/// A named area of interest inside one local frame. Sectors are contiguous:
/// crossing a boundary is a change of active area, not a scene load. The server
/// uses distance-based interest management; the client uses the same boundaries
/// for the current-sector readout.
/// </summary>
public sealed class SectorDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SectorKind Kind { get; init; }
    public required Vec3d Center { get; init; }
    public required double Radius { get; init; }
    public required IReadOnlyList<StaticBody> Bodies { get; init; }
}

public sealed class WorldCatalog
{
    public const double WarpRange = 21000.0;
    public const double ScanRange = 4200.0;
    /// <summary>Dynamic entities farther than this are not sent to a client.</summary>
    public const double RelevanceRange = 15000.0;
    public const double MaxSpeed = 260.0;
    public const double CruiseSpeed = 20_000;
    public const double CruiseClearance = 10_000;
    public const double MaxBoostSpeed = 720.0;
    public const float StartFuel = 100f;

    /// <summary>Altitude of the station above the planet surface, in metres.</summary>
    public const double StationOrbitAltitude = 1_400_000.0;

    public required int Seed { get; init; }
    public required string SystemName { get; init; }
    public required string SectorId { get; init; }

    /// <summary>Flavour of the local sector; future generation can vary density and rules.</summary>
    public required SectorKind SectorKind { get; init; }

    /// <summary>Real system position of the local sector origin (station orbit).</summary>
    public required Vec3d LocalFrameOrigin { get; init; }

    /// <summary>Star at the system barycentre. Real radius and distance.</summary>
    public required CelestialBody Star { get; init; }

    /// <summary>Planet the station orbits. Real radius and distance from the star.</summary>
    public required CelestialBody Planet { get; init; }

    /// <summary>Station in sector-local metres.</summary>
    public required StaticBody Station { get; init; }

    public required IReadOnlyList<StaticBody> Asteroids { get; init; }

    /// <summary>Named areas of interest within the local frame.</summary>
    public required IReadOnlyList<SectorDefinition> Sectors { get; init; }

    /// <summary>Returns the sector containing the position, or the nearest one.</summary>
    public SectorDefinition SectorAt(Vec3d local)
    {
        var best = Sectors[0];
        var bestDistance = double.MaxValue;
        foreach (var sector in Sectors)
        {
            var distance = Vec3d.Distance(local, sector.Center);
            if (distance <= sector.Radius)
                return sector;
            var outside = distance - sector.Radius;
            if (outside < bestDistance)
            {
                bestDistance = outside;
                best = sector;
            }
        }

        return best;
    }

    public SectorDefinition? FindSector(string id)
    {
        foreach (var sector in Sectors)
        {
            if (string.Equals(sector.Id, id, StringComparison.Ordinal))
                return sector;
        }

        return null;
    }

    /// <summary>Nearby contacts that the authoritative simulation can target.</summary>
    public IReadOnlyList<StaticBody> LocalBodies
    {
        get
        {
            var all = new List<StaticBody>(Asteroids.Count + 1) { Station };
            all.AddRange(Asteroids);
            return all;
        }
    }

    private IReadOnlyDictionary<int, StaticBody>? _localById;
    private IReadOnlyDictionary<int, CelestialBody>? _celestialById;

    public StaticBody? Find(int id)
    {
        _localById ??= LocalBodies.ToDictionary(b => b.Id);
        return _localById.TryGetValue(id, out var body) ? body : null;
    }

    public CelestialBody? FindCelestial(int id)
    {
        _celestialById ??= new[] { Star, Planet }.ToDictionary(b => b.Id);
        return _celestialById.TryGetValue(id, out var body) ? body : null;
    }

    /// <summary>Real vector from the sector origin to a celestial body, in metres.</summary>
    public Vec3d CelestialOffset(CelestialBody body) => body.SystemPosition - LocalFrameOrigin;

    /// <summary>
    /// Angular radius of a body as seen from the sector origin. Used by the renderer to
    /// preserve apparent size when the body is projected into the local display frame.
    /// </summary>
    public double AngularRadius(CelestialBody body)
    {
        var distance = CelestialOffset(body).Length;
        return distance <= body.Radius ? Math.PI / 2 : Math.Asin(body.Radius / distance);
    }

    public static WorldCatalog Generate(int seed)
    {
        var random = new Random(seed);

        // Real astronomical values: a Sun-like star one AU away and an Earth-sized
        // planet with a fictional ring system. The station sits in a low polar orbit.
        const double au = 149_597_870_700.0;
        var starRadius = 696_000_000.0;
        var planetRadius = 7_450_000.0;
        var planetPosition = new Vec3d(au, 0, 0);

        var star = new CelestialBody
        {
            Id = 1,
            Kind = CelestialKind.Star,
            Name = "Vesper",
            SystemPosition = Vec3d.Zero,
            Radius = starRadius,
        };

        var planet = new CelestialBody
        {
            Id = 2,
            Kind = CelestialKind.Planet,
            Name = "Aster Reach",
            SystemPosition = planetPosition,
            Radius = planetRadius,
            AxialTilt = 0.62f,
            AtmosphereHeight = 140_000,
            RingInnerRadii = 1.55,
            RingOuterRadii = 2.35,
        };

        // Sector origin: station in a polar orbit above the planet's day/night terminator.
        var localFrameOrigin = planetPosition + new Vec3d(0, planetRadius + StationOrbitAltitude, 0);

        var station = new StaticBody
        {
            Id = 3,
            Kind = BodyKind.Station,
            Name = "Kestrel Reach Station",
            Position = Vec3d.Zero,
            Radius = 160,
            Rotation = -0.55f,
        };

        // A local debris belt around the station, a few kilometres across.
        var asteroids = new List<StaticBody>(400);
        const int asteroidCount = 260;
        for (var i = 0; i < asteroidCount; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2;
            var radius = 3800 + random.NextDouble() * 3600;
            var y = (random.NextDouble() - 0.5) * 1300;
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

        var primaryAsteroids = new ReadOnlyCollection<StaticBody>(asteroids.ToArray());

        // A second, contiguous area of interest: a denser outer belt 18 km from
        // the station. It is reachable by short warp or by flying, and is used to
        // demonstrate streaming without loading a separate scene.
        var beltCenter = new Vec3d(0, 0, 18_000);
        var beltAsteroids = new List<StaticBody>(140);
        const int beltCount = 120;
        for (var i = 0; i < beltCount; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2;
            var radius = 3000 + random.NextDouble() * 1200;
            var y = (random.NextDouble() - 0.5) * 900;
            var size = 8 + Math.Pow(random.NextDouble(), 2.0) * 55;
            beltAsteroids.Add(new StaticBody
            {
                Id = 2000 + i,
                Kind = BodyKind.Asteroid,
                Name = $"Asteroid KB-{2000 + i}",
                Position = beltCenter + new Vec3d(Math.Cos(angle) * radius, y, Math.Sin(angle) * radius),
                Radius = size,
                Rotation = (float)(random.NextDouble() * Math.PI * 2),
                Variant = random.Next(0, 3),
            });
        }

        var beltBodies = new ReadOnlyCollection<StaticBody>(beltAsteroids);
        var allAsteroids = new ReadOnlyCollection<StaticBody>(
            primaryAsteroids.Concat(beltBodies).ToArray());

        var orbitSector = new SectorDefinition
        {
            Id = "kestrel-reach-orbit",
            Name = "Kestrel Reach Orbit",
            Kind = SectorKind.Planetary,
            Center = Vec3d.Zero,
            Radius = 9000,
            Bodies = new ReadOnlyCollection<StaticBody>(
                new[] { station }.Concat(primaryAsteroids).ToArray()),
        };

        var beltSector = new SectorDefinition
        {
            Id = "kestrel-reach-belt",
            Name = "Kestrel Reach Belt",
            Kind = SectorKind.Asteroid,
            Center = beltCenter,
            Radius = 4500,
            Bodies = beltBodies,
        };

        return new WorldCatalog
        {
            Seed = seed,
            SystemName = "Vesper — Kestrel Reach",
            SectorId = orbitSector.Id,
            SectorKind = orbitSector.Kind,
            LocalFrameOrigin = localFrameOrigin,
            Star = star,
            Planet = planet,
            Station = station,
            Asteroids = allAsteroids,
            Sectors = new ReadOnlyCollection<SectorDefinition>(new[] { orbitSector, beltSector }),
        };
    }
}
