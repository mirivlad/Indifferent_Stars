using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Server;

/// <summary>
/// Single-threaded authoritative UDP server. Commands are validated against the
/// session's own ship; snapshots are generated at a fixed rate from simulation state.
/// </summary>
public sealed class UdpGameServer : IDisposable
{
    private readonly ServerConfig _config;
    private readonly WorldCatalog _catalog;
    private readonly GameWorld _world;
    private readonly Socket _socket;
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<Guid, PlayerSession> _sessions = new();
    private readonly Dictionary<IPEndPoint, PlayerSession> _sessionsByEndpoint = new();
    private readonly byte[] _receiveBuffer = new byte[ProtocolConstants.MaxPacketSize + 1];
    private readonly Random _random = new();

    private bool _running = true;
    private double _lastTickSeconds;
    private double _lastSnapshotSeconds;
    private double _lastMaintenanceSeconds;
    private double _lastStatsSeconds;
    private double _startedAtSeconds;

    public UdpGameServer(ServerConfig config, WorldCatalog catalog, GameWorld world)
    {
        _config = config;
        _catalog = catalog;
        _world = world;

        var address = IPAddress.Parse(config.BindAddress);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(address, config.Port));
        _socket.Blocking = false;
    }

    public void Run(CancellationToken cancellationToken = default)
    {
        _clock.Start();
        _lastStatsSeconds = _clock.Elapsed.TotalSeconds;
        Log($"Listening on udp {LocalEndpoint}");
        Log($"System '{_catalog.SystemName}' seed={_catalog.Seed}, {_catalog.Asteroids.Count} asteroids, {_world.Npcs.Count} NPC ships.");
        Log($"Tick {_config.TickRate} Hz, snapshots {_config.SnapshotRate} Hz, protocol v{ProtocolConstants.Version}. Ctrl+C to stop.");

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            var now = _clock.Elapsed.TotalSeconds;
            var tickInterval = 1.0 / _config.TickRate;

            if (now - _lastTickSeconds > 0.5)
                _lastTickSeconds = now - tickInterval;

            var catchUp = 0;
            while (now - _lastTickSeconds >= tickInterval && catchUp < 8)
            {
                _world.Tick(tickInterval);
                _lastTickSeconds += tickInterval;
                catchUp++;
            }

            ReceivePending(now);

            if (now - _lastSnapshotSeconds >= 1.0 / _config.SnapshotRate)
            {
                BroadcastSnapshot(now);
                _lastSnapshotSeconds = now;
            }

            if (now - _lastMaintenanceSeconds >= 0.5)
            {
                RunMaintenance(now);
                _lastMaintenanceSeconds = now;
            }

            if (now - _lastStatsSeconds >= 30)
            {
                _lastStatsSeconds = now;
                Log($"tick={_world.TickCount} players={_sessions.Count} npcs={_world.Npcs.Count} uptime={(now - _startedAtSeconds):F0}s");
            }

            Thread.Sleep(1);
        }

        Log("Shutting down server.");
    }

    public void Stop() => _running = false;

    /// <summary>Bound UDP endpoint; useful when the server is started on port 0.</summary>
    public IPEndPoint LocalEndpoint => (IPEndPoint)_socket.LocalEndPoint!;

    private void ReceivePending(double now)
    {
        while (true)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int received;
            try
            {
                if (!_socket.Poll(0, SelectMode.SelectRead))
                    return;
                received = _socket.ReceiveFrom(_receiveBuffer, ref remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (received <= 0 || remote is not IPEndPoint endpoint)
                continue;

            HandlePacket(_receiveBuffer.AsSpan(0, received), endpoint, now);
        }
    }

    private void HandlePacket(ReadOnlySpan<byte> data, IPEndPoint endpoint, double now)
    {
        if (!ProtocolCodec.TryDecode(data, out var message, out var error) || message is null)
        {
            if (ProtocolCodec.TryReadHeader(data, out var version, out var type) &&
                type == MessageType.Hello && version != ProtocolConstants.Version)
            {
                Send(endpoint, new HelloRejectedMessage(
                    $"Protocol mismatch: client v{version}, server v{ProtocolConstants.Version}. Update the client."));
            }

            if (!string.IsNullOrEmpty(error))
                Log($"{endpoint}: rejected packet — {error}");
            return;
        }

        switch (message)
        {
            case HelloMessage hello:
                HandleHello(hello, endpoint, now);
                break;

            case InputMessage input:
                if (_sessionsByEndpoint.TryGetValue(endpoint, out var session))
                {
                    session.LastSeenSeconds = now;
                    var newer = input.Sequence > session.LastInputSequence ||
                                session.LastInputSequence - input.Sequence > 1000;
                    if (newer)
                    {
                        session.LastInputSequence = input.Sequence;
                        _world.ApplyInput(session.PlayerId, InputState.FromWire(
                            input.Throttle, input.Steer, input.Pitch, input.Vertical, input.Boost, input.Brake, input.Strafe, input.Roll, input.FlightAssist));
                    }
                }
                break;

            case ScanRequestMessage scanRequest:
                HandleScan(scanRequest, endpoint, now);
                break;

            case ApproachRequestMessage approach:
                if (_sessionsByEndpoint.TryGetValue(endpoint, out var pilot))
                {
                    pilot.LastSeenSeconds = now;
                    Send(endpoint, new ApproachResultMessage(approach.TargetId, _world.StartApproach(pilot.PlayerId, approach.TargetId, approach.Follow)));
                }
                break;

            case DockRequestMessage dock:
                if (_sessionsByEndpoint.TryGetValue(endpoint, out var dockSession))
                {
                    dockSession.LastSeenSeconds = now;
                    var accepted = dock.Undock ? _world.Undock(dockSession.PlayerId) : _world.RequestDocking(dockSession.PlayerId, dock.TargetId);
                    Send(endpoint, new DockResultMessage(dock.Undock, accepted));
                    if (accepted && !dock.Undock && _world.GetMarket(dockSession.PlayerId) is { } market)
                        Send(endpoint, new MarketStateMessage(0, market));
                }
                break;

            case MarketRequestMessage marketRequest:
                if (_sessionsByEndpoint.TryGetValue(endpoint, out var marketSession) &&
                    _world.GetMarket(marketSession.PlayerId) is { } marketState)
                {
                    marketSession.LastSeenSeconds = now;
                    Send(endpoint, new MarketStateMessage(marketRequest.RequestId, marketState));
                }
                break;

            case TradeRequestMessage tradeRequest:
                HandleTrade(tradeRequest, endpoint, now);
                break;

            case WarpRequestMessage warpRequest:
                HandleWarp(warpRequest, endpoint, now);
                break;

            case PingMessage ping:
                Send(endpoint, new PongMessage(ping.ClientTimeMs));
                break;

            case DisconnectMessage:
                if (_sessionsByEndpoint.TryGetValue(endpoint, out var leaving))
                {
                    RemoveSession(leaving, "client disconnected");
                }
                break;
        }
    }

    private void HandleHello(HelloMessage hello, IPEndPoint endpoint, double now)
    {
        if (_sessions.TryGetValue(hello.ClientId, out var existing))
        {
            if (!existing.Endpoint.Equals(endpoint))
            {
                _sessionsByEndpoint.Remove(existing.Endpoint);
                existing.Endpoint = endpoint;
                _sessionsByEndpoint[endpoint] = existing;
                Log($"{existing.Name} reconnected from {endpoint}.");
            }
            existing.LastSeenSeconds = now;
            SendWelcome(existing);
            return;
        }

        if (_sessions.Count >= _config.MaxPlayers)
        {
            Send(endpoint, new HelloRejectedMessage("Server is full."));
            return;
        }

        var name = SanitizeName(hello.ClientName);
        var ship = _world.AddPlayer(name);
        var session = new PlayerSession
        {
            ClientId = hello.ClientId,
            PlayerId = ship.Id,
            Name = ship.Name,
            Endpoint = endpoint,
            Ship = ship,
            LastSeenSeconds = now,
        };

        _sessions[hello.ClientId] = session;
        _sessionsByEndpoint[endpoint] = session;
        _startedAtSeconds = _startedAtSeconds == 0 ? now : _startedAtSeconds;
        SendWelcome(session);
        Log($"{ship.Name} joined as ship #{ship.Id} from {endpoint} (players={_sessions.Count}).");
    }

    private void SendWelcome(PlayerSession session)
    {
        var ship = session.Ship;
        Send(session.Endpoint, new HelloAcceptedMessage(
            session.PlayerId,
            session.Name,
            ship.Position,
            (float)ship.Yaw,
            _catalog.Seed,
            _catalog.SystemName,
            (float)WorldCatalog.ScanRange,
            (float)WorldCatalog.WarpRange,
            (float)WorldCatalog.MaxBoostSpeed,
            _world.TickCount,
            (byte)_config.TickRate));
    }

    private void HandleScan(ScanRequestMessage request, IPEndPoint endpoint, double now)
    {
        if (!_sessionsByEndpoint.TryGetValue(endpoint, out var session))
            return;

        session.LastSeenSeconds = now;
        if (now - session.LastScanAtSeconds < 0.12)
            return;
        session.LastScanAtSeconds = now;

        var result = _world.Scan(session.PlayerId, request.TargetId);
        var russian = _world.Scan(session.PlayerId, request.TargetId, "ru");
        Send(endpoint, new ScanResultMessage(
            request.RequestId,
            result.TargetId,
            result.Success,
            result.Title,
            result.Body,
            russian.Title,
            russian.Body));

        if (result.Success)
            Log($"{session.Name} scanned {result.Title}.");
    }

    private void HandleWarp(WarpRequestMessage request, IPEndPoint endpoint, double now)
    {
        if (!_sessionsByEndpoint.TryGetValue(endpoint, out var session))
            return;

        session.LastSeenSeconds = now;
        if (session.LastWarpResult is { } previous && previous.RequestId == request.RequestId)
        {
            Send(endpoint, previous);
            return;
        }
        if (now - session.LastWarpAtSeconds < 0.4)
            return;
        session.LastWarpAtSeconds = now;

        var result = _world.Warp(session.PlayerId, request.TargetId, request.ArrivalDistance);
        var response = new WarpResultMessage(
            request.RequestId,
            result.TargetId,
            result.Success,
            result.Message,
            result.NewPosition,
            result.Yaw,
            result.Pitch,
            result.RussianMessage);
        session.LastWarpResult = response;
        Send(endpoint, response);

        Log($"{session.Name} warp request -> {result.Message}");
    }

    private void HandleTrade(TradeRequestMessage request, IPEndPoint endpoint, double now)
    {
        if (!_sessionsByEndpoint.TryGetValue(endpoint, out var session))
            return;

        session.LastSeenSeconds = now;
        if (session.LastTradeResult is { } previous && previous.RequestId == request.RequestId)
        {
            Send(endpoint, previous);
            return;
        }

        var outcome = _world.Trade(session.PlayerId, request.CommodityId, request.Quantity, request.Buy);
        var state = outcome.State ?? new MarketState(0, 0, 0, Array.Empty<MarketEntry>());
        var response = new TradeResultMessage(
            request.RequestId,
            outcome.Success,
            outcome.Message,
            outcome.RussianMessage,
            state);
        session.LastTradeResult = response;
        Send(endpoint, response);
        Log($"{session.Name} trade {(request.Buy ? "buy" : "sell")} #{request.CommodityId} ×{request.Quantity}: {outcome.Message}");
    }

    private void BroadcastSnapshot(double now)
    {
        if (_sessions.Count == 0)
            return;

        var serverTimeMs = (ulong)_clock.Elapsed.TotalMilliseconds;
        foreach (var session in _sessions.Values)
        {
            try
            {
                var snapshot = new SnapshotMessage(
                    _world.TickCount,
                    serverTimeMs,
                    _world.BuildSnapshot(session.PlayerId));
                foreach (var packet in ProtocolCodec.EncodeSnapshotPackets(snapshot))
                    _socket.SendTo(packet, session.Endpoint);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                Log($"Snapshot to {session.Name} failed: {ex.Message}");
            }
        }
    }

    private void RunMaintenance(double now)
    {
        List<PlayerSession>? expired = null;
        foreach (var session in _sessions.Values)
        {
            if (now - session.LastSeenSeconds > ProtocolConstants.SessionTimeout.TotalSeconds)
                (expired ??= new List<PlayerSession>()).Add(session);
        }

        if (expired is null)
            return;

        foreach (var session in expired)
            RemoveSession(session, "timed out");
    }

    private void RemoveSession(PlayerSession session, string reason)
    {
        _sessions.Remove(session.ClientId);
        _sessionsByEndpoint.Remove(session.Endpoint);
        _world.RemovePlayer(session.PlayerId);
        Log($"{session.Name} left ({reason}); players={_sessions.Count}.");
    }

    private void Send(IPEndPoint endpoint, IProtocolMessage message)
    {
        try
        {
            var bytes = ProtocolCodec.Encode(message);
            _socket.SendTo(bytes, endpoint);
        }
        catch (ArgumentException ex)
        {
            Log($"Message encode error: {ex.Message}");
        }
        catch (SocketException ex)
        {
            Log($"Send to {endpoint} failed: {ex.SocketErrorCode}");
        }
    }

    private static string SanitizeName(string name)
    {
        var cleaned = new string((name ?? string.Empty)
            .Where(c => !char.IsControl(c))
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            return "Captain";
        return cleaned.Length <= 24 ? cleaned : cleaned[..24];
    }

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

    public void Dispose() => _socket.Dispose();
}
