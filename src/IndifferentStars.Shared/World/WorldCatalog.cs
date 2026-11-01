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

public sealed class WorldCatalog
{
    // Local sector gameplay limits (metres). The sector is intentionally small enough
    // for a readable arcade dogfight; interplanetary travel is a later milestone.
    public const double PlayRadius = 8500.0;
    public const double WarpRange = 21000.0;
    public const double ScanRange = 4200.0;
    public const double MaxSpeed = 260.0;
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
        var asteroids = new List<StaticBody>(280);
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

        return new WorldCatalog
        {
            Seed = seed,
            SystemName = "Vesper — Kestrel Reach",
            SectorId = "kestrel-reach-orbit",
            SectorKind = SectorKind.Planetary,
            LocalFrameOrigin = localFrameOrigin,
            Star = star,
            Planet = planet,
            Station = station,
            Asteroids = new ReadOnlyCollection<StaticBody>(asteroids),
        };
    }
}