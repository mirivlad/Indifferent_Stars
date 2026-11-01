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
        var outward = station.Normalized();
        var side = Vec3d.Cross(Vec3d.Up, outward).Normalized();
        var position = station - outward * (480 + slot * 95) + side * (slot * 85) + Vec3d.Up * (50 + slot * 28);
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
        slot.AgeSeconds = 0;
    }

    public void Tick(double dt)
    {
        foreach (var (id, ship) in _players)
        {
            var slot = _inputs[id];
            slot.AgeSeconds += dt;
            var input = slot.AgeSeconds <= InputTimeoutSeconds ? slot.Input : InputState.None;
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
        var distance = npc is not null
            ? Vec3d.Distance(player.Position, npc.Position)
            : Catalog.Find((int)targetId) is { } body
                ? Math.Max(0, Vec3d.Distance(player.Position, body.Position) - body.Radius)
                : double.NaN;

        if (double.IsNaN(distance))
            return new ScanOutcome(false, targetId, "No contact", "The selected object is not in the scanner database.");

        if (distance > WorldCatalog.ScanRange)
            return new ScanOutcome(
                false,
                targetId,
                "Out of range",
                $"Scanner range is {WorldCatalog.ScanRange / 1000:F1} km, contact is {distance / 1000:F1} km away.");

        if (npc is not null)
            return new ScanOutcome(true, targetId, $"{npc.Role}: {npc.Name}", DescribeVessel(npc));

        var staticBody = Catalog.Find((int)targetId)!;
        return new ScanOutcome(true, targetId, staticBody.Name, DescribeBody(staticBody, distance));
    }

    public WarpOutcome Warp(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        if (player is null)
            return WarpFail(targetId, "Unknown or unowned ship.");

        var npc = _npcs.FirstOrDefault(n => n.Id == targetId);
        var body = Catalog.Find((int)targetId);
        if (npc is null && body is null)
            return WarpFail(targetId, "Warp target is not a known contact.");

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
        player.Yaw = yaw;
        player.Pitch = pitch;
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

    private static EntitySnapshot ToSnapshot(ShipState ship) => new(
        ship.Id,
        ship.IsPlayer ? EntityKind.Player : EntityKind.Npc,
        ship.Name,
        ship.Position,
        (float)ship.Yaw,
        (float)ship.Pitch,
        ship.Velocity,
        (sbyte)Math.Clamp(Math.Round(ship.Throttle * 127), -127, 127));

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
                "Docking interface is not part of First Flight; station services are telemetry only.",
            BodyKind.Planet =>
                "Gas giant — class II\n" +
                $"Radius: {body.Radius / 1000:F2} km\n" +
                "Ring system detected: two broad ice bands.\n" +
                "Atmospheric probe telemetry nominal.",
            BodyKind.Star =>
                "G-type main-sequence star\n" +
                $"Photosphere radius: {body.Radius / 1000:F2} km\n" +
                "Coronal activity: low. No flare risk in the next cycle.",
            _ => "Unclassified object.",
        };
    }

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

        var planet = Catalog.Planet.Position;
        var midpoint = (station + planet) / 2 + new Vec3d(0, 500, 0);
        var nearPlanet = planet + (station - planet).Normalized() * 4200 + new Vec3d(0, 350, 0);
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
                new Vec3d(5200, 260, 2200),
                new Vec3d(7600, -150, 3900),
                new Vec3d(8200, 320, 900),
                new Vec3d(6100, 120, -1400),
                new Vec3d(4300, -220, 900),
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