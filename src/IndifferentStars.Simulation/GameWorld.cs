using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record ScanOutcome(bool Success, uint TargetId, string Title, string Body);

public sealed record WarpOutcome(
    bool Success,
    uint TargetId,
    string Message,
    Vec3d NewPosition,
    float Yaw,
    float Pitch,
    string RussianMessage = "");

/// <summary>
/// Authoritative, engine-independent world state. Fixed-step and free of Godot types.
/// </summary>
public sealed class GameWorld
{
    private sealed class PlayerInputSlot
    {
        public InputState Input = InputState.None;
        public double AgeSeconds = 999;
    }

    private readonly Dictionary<uint, ShipState> _players = new();
    private readonly Dictionary<uint, PlayerInputSlot> _inputs = new();
    private readonly List<ShipState> _npcs = new();
    private uint _nextPlayerId = 10000;

    public GameWorld(WorldCatalog catalog)
    {
        Catalog = catalog;
        SpawnNpcs();
    }

    public WorldCatalog Catalog { get; }
    public ulong TickCount { get; private set; }
    public double TimeSeconds { get; private set; }
    public IReadOnlyDictionary<uint, ShipState> Players => _players;
    public IReadOnlyList<ShipState> Npcs => _npcs;
    public double InputTimeoutSeconds { get; set; } = 0.6;

    public ShipState AddPlayer(string name)
    {
        var slot = _players.Count;
        var station = Catalog.Station.Position;
        // Player slot on a wide parking arc in front of the station, facing the planet below.
        var angle = 0.35 + slot * 1.35;
        var radius = 900.0 + slot * 130.0;
        var position = station + new Vec3d(Math.Cos(angle) * radius, 260 + slot * 70, Math.Sin(angle) * radius);
        var toStation = (station - position).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(toStation);

        var ship = new ShipState
        {
            Id = _nextPlayerId++,
            Name = string.IsNullOrWhiteSpace(name) ? $"Captain {slot + 1}" : name,
            IsPlayer = true,
            Position = position,
            Yaw = yaw,
            Pitch = pitch,
            Velocity = Vec3d.Zero,
        };

        _players.Add(ship.Id, ship);
        _inputs[ship.Id] = new PlayerInputSlot();
        return ship;
    }

    public bool RemovePlayer(uint playerId)
    {
        _inputs.Remove(playerId);
        return _players.Remove(playerId);
    }

    public ShipState? GetPlayer(uint playerId) => _players.GetValueOrDefault(playerId);

    public void ApplyInput(uint playerId, InputState input)
    {
        if (!_inputs.TryGetValue(playerId, out var slot))
            return;
        slot.Input = input.Clamped();
        if (_players.TryGetValue(playerId, out var assistedShip))
            assistedShip.FlightAssist = input.FlightAssist;
        if (_players.TryGetValue(playerId, out var ship) &&
            (input.Throttle != 0 || input.Steer != 0 || input.Pitch != 0 || input.Vertical != 0 || input.Strafe != 0 || input.Roll != 0 || input.Brake))
            ship.ApproachTargetId = 0;
        slot.AgeSeconds = 0;
    }

    public bool StartApproach(uint playerId, uint targetId, bool follow = false)
    {
        var player = GetPlayer(playerId);
        var body = Catalog.Find((int)targetId);
        var targetShip = _players.GetValueOrDefault(targetId) ?? _npcs.FirstOrDefault(n => n.Id == targetId);
        if (player is null || player.Docked || targetShip == player || (follow && targetShip is null) || (body is null && targetShip is null))
            return false;
        var targetPosition = targetShip?.Position ?? body!.Position;
        var targetRadius = targetShip is null ? body!.Radius : 20;
        var away = (player.Position - targetPosition).Normalized();
        if (away.LengthSquared < 0.5)
            return false;
        player.ApproachTargetId = targetId;
        player.ApproachFollowing = follow;
        player.ApproachDestination = targetPosition + away * (targetRadius + 500);
        return true;
    }

    public bool RequestDocking(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        var station = Catalog.Station;
        if (player is null || player.Docked || targetId != (uint)station.Id)
            return false;
        var away = (player.Position - station.Position).Normalized();
        if (away.LengthSquared < 0.5)
            return false;
        player.DockingGranted = true;
        player.ApproachFollowing = false;
        player.DockingPoint = station.Position + away * (station.Radius + 100);
        player.ApproachDestination = player.DockingPoint;
        player.ApproachTargetId = targetId;
        return true;
    }

    public bool Undock(uint playerId)
    {
        var player = GetPlayer(playerId);
        if (player is null || !player.Docked)
            return false;
        player.Docked = false;
        player.DockingGranted = false;
        return true;
    }

    public void Tick(double dt)
    {
        foreach (var (id, ship) in _players)
        {
            var slot = _inputs[id];
            slot.AgeSeconds += dt;
            if (ship.Docked)
                continue;
            if (ship.DockingGranted && ApproachPilot.Arrived(ship, ship.DockingPoint))
            {
                ship.Docked = true;
                ship.DockingGranted = false;
                ship.ApproachTargetId = 0;
                ship.Velocity = Vec3d.Zero;
                ship.Throttle = 0;
                continue;
            }
            var input = slot.AgeSeconds <= InputTimeoutSeconds ? slot.Input : InputState.None;
            if (ship.ApproachTargetId != 0)
            {
                var targetShip = ship.ApproachFollowing
                    ? _players.GetValueOrDefault(ship.ApproachTargetId) ?? _npcs.FirstOrDefault(n => n.Id == ship.ApproachTargetId)
                    : null;
                if (ship.ApproachFollowing && targetShip is null)
                {
                    ship.ApproachTargetId = 0;
                    ship.ApproachFollowing = false;
                }
                else if (targetShip is not null)
                {
                    var away = (ship.Position - targetShip.Position).Normalized();
                    if (away.LengthSquared < 0.5) away = -ship.Forward;
                    ship.ApproachDestination = targetShip.Position + away * 520;
                    input = ApproachPilot.Steer(ship, ship.ApproachDestination, targetShip.Velocity);
                }
                else if (ApproachPilot.Arrived(ship, ship.ApproachDestination))
                    ship.ApproachTargetId = 0;
                else
                    input = ApproachPilot.Steer(ship, ship.ApproachDestination);
            }
            FlightModel.StepPlayer(ship, input, dt);
        }

        foreach (var npc in _npcs)
            FlightModel.StepNpc(npc, dt);

        TickCount++;
        TimeSeconds += dt;
    }

    public EntitySnapshot[] BuildSnapshot()
    {
        var entities = new List<EntitySnapshot>(_players.Count + _npcs.Count);
        entities.AddRange(_players.Values.Select(ToSnapshot));
        entities.AddRange(_npcs.Select(ToSnapshot));
        entities.Sort((a, b) => a.Id.CompareTo(b.Id));
        return entities.ToArray();
    }

    public ScanOutcome Scan(uint playerId, uint targetId, string language = "en")
    {
        var player = GetPlayer(playerId);
        if (player is null)
            return new ScanOutcome(false, targetId, ServerText.Get(language, "scan_failed"), ServerText.Get(language, "unknown_ship"));

        var contactShip = _npcs.FirstOrDefault(n => n.Id == targetId) ?? _players.GetValueOrDefault(targetId);
        if (contactShip is not null && contactShip.Id != playerId)
        {
            var shipDistance = Vec3d.Distance(player.Position, contactShip.Position);
            if (shipDistance > WorldCatalog.ScanRange)
                return OutOfRange(targetId, shipDistance, language);
            return new ScanOutcome(true, targetId, ServerText.Get(language, "vessel_title", contactShip.Role, contactShip.Name), DescribeVessel(contactShip, language));
        }

        if (Catalog.FindCelestial((int)targetId) is { } celestial)
        {
            var realDistance = (celestial.SystemPosition - ToSystemPosition(player.Position)).Length - celestial.Radius;
            if (realDistance > WorldCatalog.ScanRange)
                return OutOfRange(targetId, realDistance, language);
            return new ScanOutcome(true, targetId, celestial.Name, DescribeCelestial(celestial, realDistance, language));
        }

        if (Catalog.Find((int)targetId) is not { } body)
            return new ScanOutcome(false, targetId, ServerText.Get(language, "no_contact"), ServerText.Get(language, "unknown_contact"));

        var distance = Math.Max(0, Vec3d.Distance(player.Position, body.Position) - body.Radius);
        if (distance > WorldCatalog.ScanRange)
            return OutOfRange(targetId, distance, language);

        return new ScanOutcome(true, targetId, body.Name, DescribeBody(body, distance, language));
    }

    public WarpOutcome Warp(uint playerId, uint targetId, ushort arrivalDistance = 1000)
    {
        var player = GetPlayer(playerId);
        if (player is null)
            return WarpFail(targetId, "unknown_ship");
        if (player.Docked)
            return WarpFail(targetId, "warp_undock");
        if (arrivalDistance is not (500 or 1000 or 5000))
            return WarpFail(targetId, "warp_invalid");

        var npc = _npcs.FirstOrDefault(n => n.Id == targetId) ?? _players.GetValueOrDefault(targetId);
        if (npc?.Id == playerId)
            return WarpFail(targetId, "warp_self");
        var body = Catalog.Find((int)targetId);
        if (npc is null && body is null)
        {
            if (Catalog.FindCelestial((int)targetId) is not null)
            {
                var realDistance = (Catalog.FindCelestial((int)targetId)!.SystemPosition - ToSystemPosition(player.Position)).Length;
                return WarpFail(targetId, "warp_celestial", realDistance / 1_000_000);
            }

            return WarpFail(targetId, "warp_unknown");
        }

        if (TimeSeconds - player.LastWarpAtSeconds < 8.0)
            return WarpFail(targetId, "warp_cooldown", 8.0 - (TimeSeconds - player.LastWarpAtSeconds));

        var targetPosition = npc?.Position ?? body!.Position;
        var targetRadius = npc is not null ? 20.0 : body!.Radius;
        var distance = Vec3d.Distance(player.Position, targetPosition);
        if (distance > WorldCatalog.WarpRange)
            return WarpFail(targetId, "warp_range", distance / 1000, WorldCatalog.WarpRange / 1000);

        var arrivalOffset = targetRadius + arrivalDistance;
        if (distance <= arrivalOffset + 250)
            return WarpFail(targetId, "warp_close");

        var direction = (targetPosition - player.Position).Normalized();
        var arrival = targetPosition - direction * arrivalOffset;
        const double clearance = 20;
        if (Catalog.LocalBodies.Any(other => Vec3d.Distance(arrival, other.Position) < other.Radius + clearance) ||
            _npcs.Any(other => Vec3d.Distance(arrival, other.Position) < 20 + clearance) ||
            _players.Values.Any(other => other.Id != playerId && Vec3d.Distance(arrival, other.Position) < 2 * clearance) ||
            new[] { Catalog.Star, Catalog.Planet }.Any(other =>
                Vec3d.Distance(ToSystemPosition(arrival), other.SystemPosition) < other.Radius + clearance))
            return WarpFail(targetId, "warp_blocked");
        var toTarget = (targetPosition - arrival).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(toTarget);

        player.Position = arrival;
        player.ApproachTargetId = 0;
        player.DockingGranted = false;
        player.Velocity = toTarget * 60.0;
        player.SetAngles(yaw, pitch);
        player.Throttle = 0.2;
        player.LastWarpAtSeconds = TimeSeconds;

        var label = npc?.Name ?? body!.Name;
        return new WarpOutcome(
            true,
            targetId,
            ServerText.Get("en", "warp_success", label, arrivalDistance / 1000.0),
            arrival,
            (float)yaw,
            (float)pitch,
            ServerText.Get("ru", "warp_success", label, arrivalDistance / 1000.0));
    }

    private static WarpOutcome WarpFail(uint targetId, string key, params object[] args) =>
        new(false, targetId, ServerText.Get("en", key, args), Vec3d.Zero, 0, 0, ServerText.Get("ru", key, args));

    private static ScanOutcome OutOfRange(uint targetId, double distance, string language) =>
        new(false,
            targetId,
            ServerText.Get(language, "out_of_range"),
            ServerText.Get(language, "scanner_range", WorldCatalog.ScanRange / 1000, distance / 1000));

    /// <summary>Promotes a sector-local position into real system coordinates (double precision).</summary>
    private Vec3d ToSystemPosition(Vec3d local) => Catalog.LocalFrameOrigin + local;

    private static EntitySnapshot ToSnapshot(ShipState ship) => new(
        ship.Id,
        ship.IsPlayer ? EntityKind.Player : EntityKind.Npc,
        ship.Name,
        ship.Position,
        (float)ship.Yaw,
        (float)ship.Pitch,
        ship.Velocity,
        (sbyte)Math.Clamp(Math.Round(ship.Throttle * 127), -127, 127),
        ship.Orientation,
        ship.ApproachTargetId,
        ship.Docked,
        ship.DockingGranted,
        ship.FlightAssist,
        ship.ApproachTargetId != 0 && ship.ApproachFollowing);

    private static string DescribeVessel(ShipState npc, string language)
    {
        var speed = npc.Velocity.Length;
        var heading = (npc.Yaw * 180.0 / Math.PI + 360) % 360;
        return ServerText.Get(language, "vessel", npc.Role, speed, heading, npc.Position.Y);
    }

    private string DescribeBody(StaticBody body, double distance, string language)
    {
        return body.Kind switch
        {
            BodyKind.Asteroid => DescribeAsteroid(body, distance, language),
            BodyKind.Station => ServerText.Get(language, "station", distance / 1000),
            _ => ServerText.Get(language, "unclassified"),
        };
    }

    private string DescribeCelestial(CelestialBody body, double distance, string language) => body.Kind switch
    {
        CelestialKind.Planet =>
            ServerText.Get(language, "planet", body.Radius / 1000,
                body.RingOuterRadii > 0 ? ServerText.Get(language, "rings") : "",
                body.AtmosphereHeight > 0 ? ServerText.Get(language, "atmosphere", body.AtmosphereHeight / 1000) : "", distance / 1000),
        CelestialKind.Star =>
            ServerText.Get(language, "star", body.Radius / 1000, distance / 1_000_000),
        _ => ServerText.Get(language, "unclassified"),
    };

    private string DescribeAsteroid(StaticBody body, double distance, string language)
    {
        var random = new Random(unchecked(Catalog.Seed * 73856093 ^ body.Id * 19349663));
        var silicate = 25 + random.Next(0, 30);
        var carbon = 15 + random.Next(0, 28);
        var ice = 8 + random.Next(0, 18);
        var iron = Math.Max(2, 100 - silicate - carbon - ice);
        var total = silicate + carbon + ice + iron;
        silicate = silicate * 100 / total;
        carbon = carbon * 100 / total;
        ice = ice * 100 / total;
        iron = Math.Max(0, 100 - silicate - carbon - ice);
        var quality = 70 + random.Next(0, 31);
        var className = ServerText.Get(language, ice > 16 ? "class_c" : silicate > 34 ? "class_s" : "class_m");
        return ServerText.Get(language, "asteroid", className, body.Radius, silicate, carbon, ice, iron, quality, distance / 1000);
    }

    private void SpawnNpcs()
    {
        var station = Catalog.Station.Position;

        AddOrbitNpc("Dawn Trader", "Courier", station, 620, 80, 1.7, 78, 0.0);
        AddOrbitNpc("Wandering Kite", "Courier", station, 1050, 180, 4.1, 96, 1.1);
        AddOrbitNpc("Pale Meridian", "Hauler", station, 1550, 120, 2.6, 58, 2.4);
        AddOrbitNpc("Quiet Ledger", "Surveyor", station, 2050, 300, 5.4, 72, 0.6);

        // Freighter lane: from the station down towards the planet's upper atmosphere.
        var midpoint = station + new Vec3d(2600, -900, -1800);
        var nearPlanet = station + new Vec3d(4200, -3400, -2600);
        _npcs.Add(CreateNpc(
            "Long Cargo",
            "Hauler",
            new[] { station + new Vec3d(400, 120, 0), midpoint, nearPlanet, midpoint, station },
            62,
            1));

        _npcs.Add(CreateNpc(
            "Cold Survey",
            "Surveyor",
            new[]
            {
                station + new Vec3d(4200, 260, 2200),
                station + new Vec3d(6400, -150, 3400),
                station + new Vec3d(6800, 320, 900),
                station + new Vec3d(4800, 120, -1400),
                station + new Vec3d(2900, -220, 900),
            },
            84,
            2));
    }

    private void AddOrbitNpc(string name, string role, Vec3d center, double radius, double height, double phase, double speed, double startPhase)
    {
        var waypoints = new Vec3d[8];
        for (var i = 0; i < waypoints.Length; i++)
        {
            var angle = startPhase + i * Math.PI * 2 / waypoints.Length;
            waypoints[i] = center + new Vec3d(
                Math.Cos(angle) * radius,
                Math.Sin(phase + i * 0.8) * height,
                Math.Sin(angle) * radius);
        }

        _npcs.Add(CreateNpc(name, role, waypoints, speed, 1));
    }

    private int _nextNpcId = 5000;

    private ShipState CreateNpc(string name, string role, IReadOnlyList<Vec3d> waypoints, double speed, int startIndex)
    {
        var route = new NpcRoute { Waypoints = waypoints, Speed = speed, Index = startIndex % waypoints.Count };
        var spawn = waypoints[0];
        var direction = (waypoints[route.Index] - spawn).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(direction);
        return new ShipState
        {
            Id = (uint)_nextNpcId++,
            Name = name,
            IsPlayer = false,
            Role = role,
            Position = spawn,
            Yaw = yaw,
            Pitch = pitch,
            Velocity = direction * speed,
            Route = route,
            Throttle = 0.7,
        };
    }
}
