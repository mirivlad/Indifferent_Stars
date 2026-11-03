using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record ScanOutcome(bool Success, uint TargetId, string Title, string Body);

public sealed record WarpOutcome(
    bool Success,
    uint TargetId,
    string Message,
    Vec3d Destination,
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
public sealed partial class GameWorld
{
    private sealed class PlayerInputSlot
    {
        public InputState Input = InputState.None;
        public double AgeSeconds = 999;
    }

    private sealed class PlayerAccount
    {
        public int StationId = 3;
        public int Credits = CommodityCatalog.StartingCredits;
        public readonly Dictionary<ushort, int> Cargo = new();
    }

    private readonly Dictionary<uint, ShipState> _players = new();
    private readonly Dictionary<uint, PlayerInputSlot> _inputs = new();
    private readonly Dictionary<uint, PlayerAccount> _accounts = new();
    private readonly Dictionary<int,Dictionary<ushort,int>> _stationStocks = new();
    private readonly List<ShipState> _npcs = new();
    private readonly IReadOnlyList<StaticBody> _collisionBodies;
    private readonly Dictionary<int, SurfacePose> _fixedSurfaces;
    private uint _nextPlayerId = 10000;

    public GameWorld(WorldCatalog catalog)
    {
        Catalog = catalog;
        _collisionBodies = catalog.LocalBodies;
        _fixedSurfaces = catalog.Asteroids.ToDictionary(body => body.Id, body => WorldSurfaces.Body(catalog, body, 0));
        foreach(var station in catalog.Stations)
            _stationStocks[station.Id]=CommodityCatalog.All.ToDictionary(c=>c.Id,c=>c.InitialStock);
        BuildCollisionRegions();
        SpawnWorkers();
    }

    public WorldCatalog Catalog { get; }
    public ulong TickCount { get; private set; }
    public double TimeSeconds { get; private set; }
    public IReadOnlyDictionary<uint, ShipState> Players => _players;
    public IReadOnlyList<ShipState> Npcs => _npcs;
    public bool MeasureTimings { get; set; }
    public double RouteMilliseconds { get; private set; }
    public double CruiseSafetyMilliseconds { get; private set; }
    public double CollisionMilliseconds { get; private set; }
    public double InputTimeoutSeconds { get; set; } = 0.6;

    public ShipState AddPlayer(string name)
    {
        var slot = _players.Count;
        var station = Catalog.Station.Position;
        // Player slot on a wide parking arc in front of the station, facing the planet below.
        var angle = 0.35 + slot * 1.35;
        var radius = 900.0 + slot * 130.0;
        var position = station + new Vec3d(Math.Cos(angle) * radius, ModelPose.StationDockingOffset.Y + slot * 70, Math.Sin(angle) * radius);
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
        _sensorHistory.Remove(playerId);
        return _players.Remove(playerId);
    }

    public ShipState? GetPlayer(uint playerId) => _players.GetValueOrDefault(playerId);

    public void ApplyInput(uint playerId, InputState input)
    {
        if (!_inputs.TryGetValue(playerId, out var slot))
            return;
        slot.Input = input.Clamped();
        if (_players.TryGetValue(playerId, out var warpingShip) && warpingShip.InWarp) return;
        if (_players.TryGetValue(playerId, out var assistedShip))
            assistedShip.FlightAssist = input.FlightAssist;
        if (_players.TryGetValue(playerId, out var ship) &&
            (input.Throttle != 0 || input.Steer != 0 || input.Pitch != 0 || input.Vertical != 0 || input.Strafe != 0 || input.Roll != 0 || input.Brake || input.Cruise))
        {
            ship.ApproachTargetId = 0;
            ship.RouteActive = false;
            ship.CancelOrbit();
        }
        slot.AgeSeconds = 0;
    }

    public bool StartMove(uint playerId, Vec3d destination)
    {
        var ship = GetPlayer(playerId);
        if (ship is null || ship.Docked || ship.InWarp || !destination.IsFinite || !double.IsFinite(Vec3d.Distance(ship.Position, destination)))
            return false;
        ship.RouteActive = false;
        ship.CancelOrbit();
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
        if (player is null || player.Docked || player.InWarp || targetShip == player || (follow && targetShip is null) || (body is null && targetShip is null) || (targetShip is not null && (targetShip.InWarp || !CanLocate(player, targetShip))))
            return false;
        var targetPosition = targetShip?.Position ?? body!.Position;
        var targetRadius = targetShip is null ? body!.Radius : 20;
        var away = (player.Position - targetPosition).Normalized();
        if (away.LengthSquared < 0.5)
            return false;
        player.RouteActive = false;
        player.CancelOrbit();
        player.ApproachTargetId = targetId;
        player.ApproachFollowing = follow;
        player.DockingGranted = false;
        player.ApproachDestination = targetPosition + away * (targetRadius + 500);
        return true;
    }

    public bool RequestDocking(uint playerId, uint targetId)
    {
        var player = GetPlayer(playerId);
        var station = Catalog.Find((int)targetId);
        if (player is null || player.Docked || player.InWarp || station?.Kind != BodyKind.Station)
            return false;
        return GrantDocking(player,station);
    }

    private bool GrantDocking(ShipState player,StaticBody station)
    {
        var targetId=(uint)station.Id;
        if (_players.Values.Concat(_npcs).Any(other => other.Id!=player.Id && (other.Docked||other.DockingGranted) && other.DockingStationId==targetId))return false;
        player.DockingStationId = targetId;
        player.DockingGranted = true;
        player.ApproachFollowing = false;
        player.DockingPoint = station.Position + ModelPose.StationDockingOffset;
        player.ApproachDestination = player.DockingPoint;
        player.RouteActive = false;
        player.CancelOrbit();
        player.ApproachTargetId = targetId;
        return true;
    }

    public bool Undock(uint playerId)
    {
        var player = GetPlayer(playerId);
        if (player is null || !player.Docked)
            return false;
        player.Docked = false;
        player.DockingStationId = 0;
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

        var stationStock = _stationStocks[account.StationId];
        var stock = stationStock.GetValueOrDefault(commodityId);
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
            stationStock[commodityId] = stock - quantity;
            return TradeSuccess(account, "trade_bought", name, quantity, total);
        }

        if (held < quantity)
            return TradeFail(account, "trade_no_cargo", name);
        var revenue = commodity.SellPrice * quantity;
        account.Credits += revenue;
        account.Cargo[commodityId] = held - quantity;
        stationStock[commodityId] = stock + quantity;
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
                _stationStocks[account.StationId].GetValueOrDefault(commodity.Id),
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
        var warping = _players.Values.Concat(_npcs).Where(ship => ship.InWarp).Select(ship => ship.Id).ToHashSet();
        var starts = _players.Values.Concat(_npcs).ToDictionary(ship => ship.Id, ship => (ship.Position, ship.Orientation));
        foreach (var (id, ship) in _players)
        {
            var slot = _inputs[id];
            slot.AgeSeconds += dt;
            if (ship.InWarp)
            {
                StepWarp(ship, dt);
                continue;
            }
            if (ship.Docked)
                continue;
            if (ship.DockingGranted && ApproachPilot.Arrived(ship, ship.DockingPoint))
            {
                ship.Docked = true;
                _accounts[ship.Id].StationId=(int)ship.DockingStationId;
                ship.DockingGranted = false;
                ship.ApproachTargetId = 0;
                ship.Velocity = Vec3d.Zero;
                ship.Throttle = 0;
                continue;
            }
            if (StepOrbit(ship, dt)) continue;
            var input = slot.AgeSeconds <= InputTimeoutSeconds ? slot.Input : InputState.None;
            if (ship.RouteActive)
            {
                input = StepRoute(ship, dt);
                if (ship.InWarp) { warping.Add(ship.Id); continue; }
            }
            else if (ship.ApproachTargetId != 0)
            {
                var targetShip = ship.ApproachFollowing
                    ? _players.GetValueOrDefault(ship.ApproachTargetId) ?? _npcs.FirstOrDefault(n => n.Id == ship.ApproachTargetId)
                    : null;
                if (ship.ApproachFollowing && (targetShip is null || targetShip.InWarp || !CanLocate(ship, targetShip)))
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
            ship.CruiseSpeedLimit = CruiseLimitAt(ship);
            var cruiseSafe = (!input.Cruise && !ship.Cruising && ship.Velocity.Length <= WorldCatalog.MaxBoostSpeed) || CanCruise(ship);
            if (!cruiseSafe && (ship.Cruising || ship.Velocity.Length > WorldCatalog.MaxBoostSpeed))
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
                input = input with { Cruise = false, Throttle = 0, Brake = true };
            }
            FlightModel.StepPlayer(ship, input, dt);
        }

        foreach (var npc in _npcs)
            StepWorker(npc,dt,warping);

        var ships = _players.Values.Concat(_npcs).OrderBy(ship => ship.Id).ToArray();
        foreach (var ship in ships)
        {
            if (ship.Docked || warping.Contains(ship.Id)) { ship.AngularVelocity=Vec3d.Zero; continue; }
            var speed=ship.AngularVelocity.Length;
            if (speed>1e-5)
            {
                var axis=ship.AngularVelocity/speed;
                ship.Orientation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(new Vector3((float)axis.X,(float)axis.Y,(float)axis.Z),(float)(speed*dt))*ship.Orientation);
                ship.AngularVelocity*=Math.Exp(-2.5*dt);
            }
        }
        var collisionStarted = MeasureTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ResolveCollisions(ships,starts,warping,dt);
        if (MeasureTimings) CollisionMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(collisionStarted).TotalMilliseconds;

        TickCount++;
        TimeSeconds += dt;
    }

    private SurfacePose BodySurface(StaticBody body, double time) =>
        _fixedSurfaces.TryGetValue(body.Id, out var pose) ? pose : WorldSurfaces.Body(Catalog, body, time);

    public EntitySnapshot[] BuildSnapshot()
    {
        var entities = new List<EntitySnapshot>(_players.Count + _npcs.Count);
        entities.AddRange(_players.Values.Select(ToSnapshot));
        entities.AddRange(_npcs.Select(ToSnapshot));
        entities.Sort((a, b) => a.Id.CompareTo(b.Id));
        return entities.ToArray();
    }

    public double CruiseLimitAt(ShipState ship)
    {
        // Anticipate the deceleration needed to enter an inhabited area at its speed limit.
        var excess = Math.Max(0, ship.Velocity.LengthSquared - WorldCatalog.PopulatedCruiseSpeed * WorldCatalog.PopulatedCruiseSpeed);
        var margin = excess / (2 * FlightModel.CruiseBrakeAcceleration) + ship.Velocity.Length * .1;
        return Catalog.Stations.Any(station => Vec3d.Distance(ship.Position, station.Position) <= WorldCatalog.ActivityRadius + margin)
            ? WorldCatalog.PopulatedCruiseSpeed : WorldCatalog.CruiseSpeed;
    }

    private bool CanCruise(ShipState ship)
    {
        if (!MeasureTimings) return CruiseAllowed(ship);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return CruiseAllowed(ship); }
        finally { CruiseSafetyMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    private bool CruiseAllowed(ShipState ship) =>
        !ship.Docked && (ship.ApproachTargetId == 0 || ship.RouteActive || ship.OrbitEstablished) &&
        Catalog.LocalBodies.All(body => CruiseClearOf(ship, body.Position, body.Radius)) &&
        _players.Values.All(other => other.Id == ship.Id || CruiseClearOf(ship, other.Position, 20)) &&
        _npcs.All(other => other.Id==ship.Id || CruiseClearOf(ship, other.Position, 20)) &&
        Catalog.Celestials.All(body =>
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
        var body=Catalog.Find((int)targetId);
        var station = body?.Kind==BodyKind.Station;
        var position = station ? body!.Position : npc?.Position;
        var success = player is not null && position is { } point &&
            (station ? Vec3d.Distance(player.Position, point) <= WorldCatalog.DistantSensorRange : CanLocate(player, npc!));
        var key = !success ? "comms_unavailable" : station ? "comms_station" : "comms_npc";
        var name = station ? body!.Name : npc?.Name ?? "";
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

    private static ScanOutcome OutOfRange(uint targetId, double distance, string language) =>
        new(false,
            targetId,
            ServerText.Get(language, "out_of_range"),
            ServerText.Get(language, "scanner_range", WorldCatalog.ScanRange / 1000, distance / 1000));

    /// <summary>Promotes a sector-local position into real system coordinates (double precision).</summary>
    private Vec3d ToSystemPosition(Vec3d local) => Catalog.LocalFrameOrigin + local;

    private EntitySnapshot ToSnapshot(ShipState ship) => new(
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
        ship.DockingGranted ? ship.DockingPoint : ship.ApproachTargetId == ship.Id ? ship.ApproachDestination : null,
        ship.CruiseInhibited,
        ship.CruiseBraking,
        ship.WarpStage,
        ship.WarpProgress,
        ship.InWarp ? ship.WarpDestination : null,
        !ship.InWarp && TimeSeconds-ship.ImpactAt<.6 ? ship.Impact : null,
        ship.RouteRevision, ship.RouteIndex, ship.RouteActive, ship.Boosting,
        ship.OrbitTargetId, ship.OrbitAltitude, ship.OrbitRetrograde, ship.OrbitEstablished, ship.DockingStationId,
        ship.Work is { } work?new NpcStatus(work.Occupation,work.Phase,(uint)work.TargetId):null);

    private string DescribeVessel(ShipState npc, string language)
    {
        var speed = npc.Velocity.Length;
        var heading = (npc.Yaw * 180.0 / Math.PI + 360) % 360;
        var work=npc.Work;
        var role=work is null?npc.Role:ServerText.Get(language,"npc_job_"+work.Occupation.ToString().ToLowerInvariant());
        var description=ServerText.Get(language,"vessel",role,speed,heading);
        if(work is null)return description;
        var target=Catalog.Find(work.TargetId)?.Name??Catalog.FindCelestial(work.TargetId)?.Name??Catalog.SystemName;
        return description+"\n"+ServerText.Get(language,"npc_activity",ServerText.Get(language,"npc_phase_"+work.Phase.ToString().ToLowerInvariant()),
            target,work.Cargo,work.CompletedJobs,work.Deliveries);
    }

    private string DescribeBody(StaticBody body, double distance, string language)
    {
        return body.Kind switch
        {
            BodyKind.Asteroid => DescribeAsteroid(body, distance, language),
            BodyKind.Station => ServerText.Get(language, "station", body.Name,ServerText.Get(language,"station_"+body.Purpose.ToString().ToLowerInvariant()),distance/1000),
            _ => ServerText.Get(language, "unclassified"),
        };
    }

    private string DescribeCelestial(CelestialBody body, double distance, string language) => body.Kind switch
    {
        CelestialKind.Planet or CelestialKind.Moon =>
            ServerText.Get(language, "planet", ServerText.Get(language,"surface_"+body.Surface.ToString().ToLowerInvariant()),body.Radius / 1000,
                body.RingOuterRadii > 0 ? ServerText.Get(language, "rings") : "",
                body.AtmosphereHeight > 0 ? ServerText.Get(language, "atmosphere", body.AtmosphereHeight / 1000) : "", distance / 1000),
        CelestialKind.Star =>
            ServerText.Get(language, "star", ServerText.Get(language,"stellar_"+body.StellarClass.ToString().ToLowerInvariant()),body.TemperatureKelvin,body.Radius / 1000,distance/1_000_000_000),
        _ => ServerText.Get(language, "unclassified"),
    };

    private string DescribeAsteroid(StaticBody body, double distance, string language)
    {
        var random = new SeedStream(Catalog.Seed,body.Id,$"asteroid-composition-{Catalog.SystemIndex}");
        var silicate = 25 + random.Next(0, 30);
        var carbon = 15 + random.Next(0, 28);
        var ice = body.Icy?random.Range(55,81):8+random.Range(0,18);
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

}
