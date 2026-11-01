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
    float Pitch);

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
    private uint _nextPlayerId = 1;

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
        if (_players.TryGetValue(playerId, out var ship) &&
            (input.Throttle != 0 || input.Steer != 0 || input.Pitch != 0 || input.Vertical != 0 || input.Strafe != 0 || input.Roll != 0 || input.Brake))
            ship.ApproachTargetId = 0;
        slot.AgeSeconds = 0;
    }

    public bool StartApproach(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        var body = Catalog.Find((int)targetId);
        if (player is null || player.Docked || body is null)
            return false;
        var away = (player.Position - body.Position).Normalized();
        if (away.LengthSquared < 0.5)
            return false;
        player.ApproachTargetId = targetId;
        player.ApproachDestination = body.Position + away * (body.Radius + 500);
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
                if (ApproachPilot.Arrived(ship, ship.ApproachDestination))
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

    public ScanOutcome Scan(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        if (player is null)
            return new ScanOutcome(false, targetId, "Scan failed", "Unknown or unowned ship.");

        var npc = _npcs.FirstOrDefault(n => n.Id == targetId);
        if (npc is not null)
        {
            var shipDistance = Vec3d.Distance(player.Position, npc.Position);
            if (shipDistance > WorldCatalog.ScanRange)
                return OutOfRange(targetId, shipDistance);
            return new ScanOutcome(true, targetId, $"{npc.Role}: {npc.Name}", DescribeVessel(npc));
        }

        if (Catalog.FindCelestial((int)targetId) is { } celestial)
        {
            var realDistance = (celestial.SystemPosition - ToSystemPosition(player.Position)).Length - celestial.Radius;
            if (realDistance > WorldCatalog.ScanRange)
                return OutOfRange(targetId, realDistance);
            return new ScanOutcome(true, targetId, celestial.Name, DescribeCelestial(celestial, realDistance));
        }

        if (Catalog.Find((int)targetId) is not { } body)
            return new ScanOutcome(false, targetId, "No contact", "The selected object is not in the scanner database.");

        var distance = Math.Max(0, Vec3d.Distance(player.Position, body.Position) - body.Radius);
        if (distance > WorldCatalog.ScanRange)
            return OutOfRange(targetId, distance);

        return new ScanOutcome(true, targetId, body.Name, DescribeBody(body, distance));
    }

    public WarpOutcome Warp(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        if (player is null)
            return WarpFail(targetId, "Unknown or unowned ship.");
        if (player.Docked)
            return WarpFail(targetId, "Undock before using warp.");

        var npc = _npcs.FirstOrDefault(n => n.Id == targetId);
        var body = Catalog.Find((int)targetId);
        if (npc is null && body is null)
        {
            if (Catalog.FindCelestial((int)targetId) is not null)
            {
                var realDistance = (Catalog.FindCelestial((int)targetId)!.SystemPosition - ToSystemPosition(player.Position)).Length;
                return WarpFail(targetId,
                    $"In-system warp has no solution for a target {realDistance / 1_000_000:F1} Mm away. Interplanetary travel arrives in a later milestone.");
            }

            return WarpFail(targetId, "Warp target is not a known contact.");
        }

        if (TimeSeconds - player.LastWarpAtSeconds < 8.0)
            return WarpFail(targetId, $"Warp drive is recharging ({8.0 - (TimeSeconds - player.LastWarpAtSeconds):F1} s).");

        var targetPosition = npc?.Position ?? body!.Position;
        var targetRadius = npc is not null ? 20.0 : body!.Radius;
        var distance = Vec3d.Distance(player.Position, targetPosition);
        if (distance > WorldCatalog.WarpRange)
            return WarpFail(targetId, $"Target beyond warp range ({distance / 1000:F1} km > {WorldCatalog.WarpRange / 1000:F0} km).");

        var arrivalOffset = Math.Max(650, targetRadius + 750);
        if (distance <= arrivalOffset + 250)
            return WarpFail(targetId, "Already inside safe arrival distance.");

        var direction = (targetPosition - player.Position).Normalized();
        var arrival = targetPosition - direction * arrivalOffset;
        var toTarget = (targetPosition - arrival).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(toTarget);

        player.Position = arrival;
        player.Velocity = toTarget * 60.0;
        player.SetAngles(yaw, pitch);
        player.Throttle = 0.2;
        player.LastWarpAtSeconds = TimeSeconds;

        var label = npc?.Name ?? body!.Name;
        return new WarpOutcome(
            true,
            targetId,
            $"Short warp complete — {label} at {(distance - arrivalOffset) / 1000:F0} km.",
            arrival,
            (float)yaw,
            (float)pitch);
    }

    private static WarpOutcome WarpFail(uint targetId, string message) =>
        new(false, targetId, message, Vec3d.Zero, 0, 0);

    private static ScanOutcome OutOfRange(uint targetId, double distance) =>
        new(false,
            targetId,
            "Out of range",
            $"Scanner range is {WorldCatalog.ScanRange / 1000:F1} km, contact is {distance / 1000:F1} km away.");

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
        ship.DockingGranted);

    private static string DescribeVessel(ShipState npc)
    {
        var speed = npc.Velocity.Length;
        var heading = (npc.Yaw * 180.0 / Math.PI + 360) % 360;
        return
            $"Transponder: civilian\n" +
            $"Role: {npc.Role}\n" +
            $"Speed: {speed:F0} m/s\n" +
            $"Heading: {heading:F0}°  Altitude: {npc.Position.Y:F0} m\n" +
            "No hostile intent detected. Traffic lanes are nominal.";
    }

    private string DescribeBody(StaticBody body, double distance)
    {
        return body.Kind switch
        {
            BodyKind.Asteroid => DescribeAsteroid(body, distance),
            BodyKind.Station =>
                "Orbital station — Kestrel Reach\n" +
                "Traffic control online. Reactor signature stable.\n" +
                $"Range: {distance / 1000:F2} km\n" +
                "Docking clearance available. Trading services are currently unavailable.",
            _ => "Unclassified object.",
        };
    }

    private string DescribeCelestial(CelestialBody body, double distance) => body.Kind switch
    {
        CelestialKind.Planet =>
            "Terrestrial world — class M\n" +
            $"Radius: {body.Radius / 1000:F0} km\n" +
            (body.RingOuterRadii > 0 ? "Ring system detected: two broad ice bands.\n" : "") +
            (body.AtmosphereHeight > 0 ? $"Atmosphere: {body.AtmosphereHeight / 1000:F0} km shell, breathable proxy.\n" : "") +
            $"Range to surface: {distance / 1000:F0} km",
        CelestialKind.Star =>
            "G-type main-sequence star\n" +
            $"Photosphere radius: {body.Radius / 1000:F0} km\n" +
            "Coronal activity: low. No flare risk in the next cycle.\n" +
            $"Distance: {distance / 1_000_000:F1} Mm",
        _ => "Unclassified object.",
    };

    private string DescribeAsteroid(StaticBody body, double distance)
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
        var className = ice > 16 ? "C-type carbonaceous" : silicate > 34 ? "S-type silicaceous" : "M-type metallic";

        return
            $"Asteroid — {className}\n" +
            $"Radius: {body.Radius:F1} m\n" +
            $"Composition: silicate {silicate}%, carbon {carbon}%, water ice {ice}%, nickel-iron {iron}%\n" +
            $"Scan quality: {quality}%   Range: {distance / 1000:F2} km\n" +
            "No active emissions. Prospecting yield estimate is available in a later milestone.";
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
