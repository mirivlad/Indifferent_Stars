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

public sealed record TradeOutcome(
    bool Success,
    string Message,
    string RussianMessage,
    MarketState? State);

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

    private sealed class PlayerAccount
    {
        public int Credits = CommodityCatalog.StartingCredits;
        public readonly Dictionary<ushort, int> Cargo = new();
    }

    private readonly Dictionary<uint, ShipState> _players = new();
    private readonly Dictionary<uint, PlayerInputSlot> _inputs = new();
    private readonly Dictionary<uint, PlayerAccount> _accounts = new();
    private readonly Dictionary<ushort, int> _stationStock = new();
    private readonly List<ShipState> _npcs = new();
    private readonly IReadOnlyList<StaticBody> _collisionBodies;
    private uint _nextPlayerId = 10000;

    public GameWorld(WorldCatalog catalog)
    {
        Catalog = catalog;
        _collisionBodies = catalog.LocalBodies;
        foreach (var commodity in CommodityCatalog.All)
            _stationStock[commodity.Id] = commodity.InitialStock;
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
        _accounts[ship.Id] = new PlayerAccount();
        return ship;
    }

    public bool RemovePlayer(uint playerId)
    {
        _inputs.Remove(playerId);
        _accounts.Remove(playerId);
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
            (input.Throttle != 0 || input.Steer != 0 || input.Pitch != 0 || input.Vertical != 0 || input.Strafe != 0 || input.Roll != 0 || input.Brake || input.Cruise))
            ship.ApproachTargetId = 0;
        slot.AgeSeconds = 0;
    }

    public bool StartMove(uint playerId, Vec3d destination)
    {
        var ship = GetPlayer(playerId);
        if (ship is null || ship.Docked || !destination.IsFinite || !double.IsFinite(Vec3d.Distance(ship.Position, destination)))
            return false;
        ship.ApproachTargetId = ship.Id;
        ship.ApproachFollowing = false;
        ship.ApproachDestination = destination;
        ship.DockingGranted = false;
        return true;
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

    /// <summary>Current station market and account snapshot for one player.</summary>
    public MarketState? GetMarket(uint playerId) =>
        _accounts.TryGetValue(playerId, out var account) ? BuildMarket(account) : null;

    /// <summary>
    /// Executes a buy or sell order against the station's in-memory stock.
    /// The server owns credits, cargo and stock; clients only send intents.
    /// </summary>
    public TradeOutcome Trade(uint playerId, ushort commodityId, int quantity, bool buy)
    {
        var player = GetPlayer(playerId);
        if (player is null || !_accounts.TryGetValue(playerId, out var account))
            return TradeFail(null, "trade_invalid");
        if (!player.Docked)
            return TradeFail(account, "trade_not_docked");
        if (quantity <= 0 || quantity > CommodityCatalog.MaxTradeQuantity)
            return TradeFail(account, "trade_invalid");

        var commodity = CommodityCatalog.Find(commodityId);
        if (commodity is null)
            return TradeFail(account, "trade_unknown");

        var stock = _stationStock.GetValueOrDefault(commodityId);
        var held = account.Cargo.GetValueOrDefault(commodityId);
        var cargoUsed = CargoUsed(account);
        var name = commodity.Key;

        if (buy)
        {
            var total = commodity.BuyPrice * quantity;
            if (total > account.Credits)
                return TradeFail(account, "trade_no_credits", name);
            if (stock < quantity)
                return TradeFail(account, "trade_no_stock", name);
            if (cargoUsed + quantity > CommodityCatalog.CargoCapacity)
                return TradeFail(account, "trade_no_space", name);

            account.Credits -= total;
            account.Cargo[commodityId] = held + quantity;
            _stationStock[commodityId] = stock - quantity;
            return TradeSuccess(account, "trade_bought", name, quantity, total);
        }

        if (held < quantity)
            return TradeFail(account, "trade_no_cargo", name);
        var revenue = commodity.SellPrice * quantity;
        account.Credits += revenue;
        account.Cargo[commodityId] = held - quantity;
        _stationStock[commodityId] = stock + quantity;
        return TradeSuccess(account, "trade_sold", name, quantity, revenue);
    }

    private MarketState BuildMarket(PlayerAccount account)
    {
        var entries = new MarketEntry[CommodityCatalog.All.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            var commodity = CommodityCatalog.All[i];
            entries[i] = new MarketEntry(
                commodity.Id,
                _stationStock.GetValueOrDefault(commodity.Id),
                account.Cargo.GetValueOrDefault(commodity.Id));
        }

        return new MarketState(account.Credits, CargoUsed(account), CommodityCatalog.CargoCapacity, entries);
    }

    private static int CargoUsed(PlayerAccount account)
    {
        var used = 0;
        foreach (var quantity in account.Cargo.Values)
            used += quantity;
        return used;
    }

    private TradeOutcome TradeSuccess(PlayerAccount account, string key, string commodityKey, int quantity, int total)
    {
        var enArgs = new object[] { ServerText.Get("en", "commodity_" + commodityKey), quantity, total };
        var ruArgs = new object[] { ServerText.Get("ru", "commodity_" + commodityKey), quantity, total };
        return new TradeOutcome(
            true,
            ServerText.Get("en", key, enArgs),
            ServerText.Get("ru", key, ruArgs),
            BuildMarket(account));
    }

    private TradeOutcome TradeFail(PlayerAccount? account, string key, string? commodityKey = null, params object[] args)
    {
        var enArgs = new List<object>(args.Length + 1);
        var ruArgs = new List<object>(args.Length + 1);
        if (commodityKey is not null)
        {
            enArgs.Add(ServerText.Get("en", "commodity_" + commodityKey));
            ruArgs.Add(ServerText.Get("ru", "commodity_" + commodityKey));
        }
        enArgs.AddRange(args);
        ruArgs.AddRange(args);

        return new TradeOutcome(
            false,
            ServerText.Get("en", key, enArgs.ToArray()),
            ServerText.Get("ru", key, ruArgs.ToArray()),
            account is null ? null : BuildMarket(account));
    }

    public void Tick(double dt)
    {
        var starts = _players.Values.Concat(_npcs).ToDictionary(ship => ship.Id, ship => ship.Position);
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
            if (!input.Cruise) ship.CruiseInhibited = false;
            var cruiseSafe = (!input.Cruise && !ship.Cruising) || CanCruise(ship);
            if (ship.Cruising && !cruiseSafe)
                ship.CruiseBraking = true;
            if (input.Cruise && (!cruiseSafe || ship.CruiseInhibited || ship.CruiseBraking))
            {
                ship.CruiseInhibited = true;
                if (ship.Cruising || ship.Velocity.Length > WorldCatalog.MaxBoostSpeed) ship.CruiseBraking = true;
                input = input with { Cruise = false };
            }
            if (ship.CruiseBraking)
            {
                if (ship.Velocity.Length <= 0.1) ship.CruiseBraking = false;
                else input = input with { Cruise = false, Throttle = 0, Brake = true };
            }
            FlightModel.StepPlayer(ship, input, dt);
        }

        foreach (var npc in _npcs)
            FlightModel.StepNpc(npc, dt);

        var ships = _players.Values.Concat(_npcs).OrderBy(ship => ship.Id).ToArray();
        foreach (var ship in ships)
        {
            if (ship.Docked) continue;
            double? first = null;
            void Check(Vec3d center, double radius)
            {
                var time = CollisionSolver.ContactTime(starts[ship.Id], ship.Position, center,
                    radius + CollisionSolver.ShipRadius);
                if (time is not null && (first is null || time < first)) first = time;
            }
            foreach (var body in _collisionBodies) Check(body.Position, body.Radius);
            Check(Catalog.CelestialOffset(Catalog.Planet), Catalog.Planet.Radius);
            Check(Catalog.CelestialOffset(Catalog.Star), Catalog.Star.Radius);
            if (first is { } hit) CollisionSolver.Stop(ship, starts[ship.Id], hit);
        }
        for (var i = 0; i < ships.Length; i++)
        for (var j = i + 1; j < ships.Length; j++)
        {
            var a = ships[i];
            var b = ships[j];
            var time = CollisionSolver.ContactTime(starts[a.Id] - starts[b.Id],
                a.Position - b.Position, Vec3d.Zero, 2 * CollisionSolver.ShipRadius);
            if (time is not { } hit) continue;
            if (!a.Docked) CollisionSolver.Stop(a, starts[a.Id], hit);
            if (!b.Docked) CollisionSolver.Stop(b, starts[b.Id], hit);
        }

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

    private bool CanCruise(ShipState ship) =>
        !ship.Docked && ship.ApproachTargetId == 0 &&
        Catalog.LocalBodies.All(body => CruiseClearOf(ship, body.Position, body.Radius)) &&
        _players.Values.All(other => other.Id == ship.Id || CruiseClearOf(ship, other.Position, 20)) &&
        _npcs.All(other => CruiseClearOf(ship, other.Position, 20)) &&
        new[] { Catalog.Star, Catalog.Planet }.All(body =>
            CruiseClearOf(ship, body.SystemPosition - Catalog.LocalFrameOrigin, body.Radius + body.AtmosphereHeight));

    private static bool CruiseClearOf(ShipState ship, Vec3d target, double radius)
    {
        var offset = target - ship.Position;
        var closing = Math.Max(0, Vec3d.Dot(ship.Velocity, offset.Normalized()));
        var stoppingDistance = closing * closing / (2 * FlightModel.CruiseBrakeAcceleration);
        return offset.Length - radius >= WorldCatalog.CruiseClearance + stoppingDistance + closing * 0.1;
    }

    /// <summary>
    /// Snapshot for one viewer. The viewer always sees its own ship; other
    /// entities are included only inside <see cref="WorldCatalog.RelevanceRange"/>.
    /// This is the first, deliberately simple interest-management slice.
    /// </summary>
    public EntitySnapshot[] BuildSnapshot(uint viewerId)
    {
        var viewer = GetPlayer(viewerId);
        if (viewer is null)
            return Array.Empty<EntitySnapshot>();

        var entities = new List<EntitySnapshot>(_players.Count + _npcs.Count)
        {
            ToSnapshot(viewer),
        };

        foreach (var player in _players.Values)
        {
            if (player.Id != viewerId &&
                Vec3d.Distance(viewer.Position, player.Position) <= WorldCatalog.RelevanceRange)
                entities.Add(ToSnapshot(player));
        }

        foreach (var npc in _npcs)
        {
            if (Vec3d.Distance(viewer.Position, npc.Position) <= WorldCatalog.RelevanceRange)
                entities.Add(ToSnapshot(npc));
        }

        entities.Sort((a, b) => a.Id.CompareTo(b.Id));
        return entities.ToArray();
    }

    public CommsResultMessage RadioBroadcast(ShipState npc) => new(npc.Id, true,
        ServerText.Get("en", "radio_npc", npc.Name), ServerText.Get("ru", "radio_npc", npc.Name));

    public CommsResultMessage Contact(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        var npc = _npcs.FirstOrDefault(ship => ship.Id == targetId);
        var station = targetId == (uint)Catalog.Station.Id;
        var position = station ? Catalog.Station.Position : npc?.Position;
        var success = player is not null && position is { } point &&
            Vec3d.Distance(player.Position, point) <= WorldCatalog.RelevanceRange;
        var key = !success ? "comms_unavailable" : station ? "comms_station" : "comms_npc";
        var name = station ? Catalog.Station.Name : npc?.Name ?? "";
        return new CommsResultMessage(targetId, success,
            ServerText.Get("en", key, name), ServerText.Get("ru", key, name));
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
        if (arrivalDistance is not (500 or 1000 or 5000 or 10000))
            return WarpFail(targetId, "warp_invalid");

        var npc = _npcs.FirstOrDefault(n => n.Id == targetId) ?? _players.GetValueOrDefault(targetId);
        if (npc?.Id == playerId)
            return WarpFail(targetId, "warp_self");
        var body = Catalog.Find((int)targetId);
        var celestial = Catalog.FindCelestial((int)targetId);
        var systemWarp = arrivalDistance == 10000;
        if (systemWarp && (!player.Cruising || player.Velocity.Length < 1000))
            return WarpFail(targetId, "warp_cruise_required");
        if (npc is null && body is null && (!systemWarp || celestial is null))
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

        var targetPosition = npc?.Position ?? body?.Position ?? (celestial!.SystemPosition - Catalog.LocalFrameOrigin);
        var targetRadius = npc is not null ? 20.0 : body?.Radius ?? (celestial!.Radius + celestial.AtmosphereHeight);
        var distance = Vec3d.Distance(player.Position, targetPosition);
        if (systemWarp && npc is not null && distance > WorldCatalog.RelevanceRange)
            return WarpFail(targetId, "warp_unknown");
        if (!systemWarp && distance > WorldCatalog.WarpRange)
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
                Vec3d.Distance(ToSystemPosition(arrival), other.SystemPosition) < other.Radius + other.AtmosphereHeight + clearance))
            return WarpFail(targetId, "warp_blocked");
        var toTarget = (targetPosition - arrival).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(toTarget);

        player.Position = arrival;
        player.ApproachTargetId = 0;
        player.DockingGranted = false;
        player.Velocity = systemWarp ? Vec3d.Zero : toTarget * 60.0;
        player.Cruising = false;
        player.CruiseInhibited = true;
        player.CruiseBraking = false;
        _inputs[playerId].Input = InputState.None;
        player.SetAngles(yaw, pitch);
        player.Throttle = systemWarp ? 0 : 0.2;
        player.LastWarpAtSeconds = TimeSeconds;

        var label = npc?.Name ?? body?.Name ?? celestial!.Name;
        return new WarpOutcome(
            true,
            targetId,
            ServerText.Get("en", systemWarp ? "warp_system_success" : "warp_success", label, arrivalDistance / 1000.0),
            arrival,
            (float)yaw,
            (float)pitch,
            ServerText.Get("ru", systemWarp ? "warp_system_success" : "warp_success", label, arrivalDistance / 1000.0));
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
        ship.ApproachTargetId != 0 && ship.ApproachFollowing,
        ship.Cruising,
        ship.ApproachTargetId == ship.Id ? ship.ApproachDestination : null,
        ship.CruiseInhibited,
        ship.CruiseBraking);

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

        if (Catalog.Sectors.Count > 1)
        {
            var belt = Catalog.Sectors[1].Center;
            AddOrbitNpc("Belt Runner", "Prospector", belt, 1500, 180, 1.3, 74, 0.4);
            AddOrbitNpc("Iron Vigil", "Patrol", belt, 2300, 220, 3.9, 92, 0.8);
        }
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
