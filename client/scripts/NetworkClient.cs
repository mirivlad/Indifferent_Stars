using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Godot;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

namespace IndifferentStars.Client;

public enum ConnectionState
{
    Connecting,
    Connected,
    Rejected,
}

/// <summary>
/// UDP transport. It owns no game state: decoded messages are queued and dispatched
/// on the Godot main thread by Poll().
/// </summary>
public sealed partial class NetworkClient : IDisposable
{
    private readonly ConcurrentQueue<IProtocolMessage> _incoming = new();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly string _playerName;
    private readonly string _host;
    private readonly int _port;

    private UdpClient? _udp;
    private Thread? _receiveThread;
    private CancellationTokenSource? _cancellation;

    private double _lastHelloSeconds;
    private double _lastInputSeconds;
    private uint _inputSequence;
    private uint _requestSequence = 1;

    public NetworkClient(string host, int port, string playerName)
    {
        _host = host;
        _port = port;
        _playerName = playerName;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Connecting;
    public string StatusText { get; private set; } = "Connecting…";
    public HelloAcceptedMessage? Welcome { get; private set; }
    public uint PlayerId => Welcome?.PlayerId ?? 0;
    public ulong LastSnapshotTick { get; private set; }
    public double LastSnapshotAgeSeconds { get; private set; } = 999;
    public int PingMs { get; private set; } = -1;
    public bool HasEverConnected { get; private set; }

    public event Action<HelloAcceptedMessage>? WelcomeReceived;
    public event Action<HelloRejectedMessage>? JoinRejected;
    public event Action<SnapshotMessage>? SnapshotReceived;
    public event Action<ScanResultMessage>? ScanCompleted;
    public event Action<WarpResultMessage>? WarpCompleted;
    public event Action<ApproachResultMessage>? ApproachCompleted;
    public event Action<DockResultMessage>? DockCompleted;
    public event Action<MarketStateMessage>? MarketReceived;
    public event Action<TradeResultMessage>? TradeCompleted;
    public event Action<string>? TransportError;

    public void Start()
    {
        try
        {
            var address = Dns.GetHostAddresses(_host);
            var endpoint = new IPEndPoint(address.Length > 0 ? address[0] : IPAddress.Loopback, _port);
            _udp = new UdpClient(endpoint.AddressFamily);
            _udp.Connect(endpoint);
            _udp.Client.ReceiveTimeout = 500;
            _cancellation = new CancellationTokenSource();
            _receiveThread = new Thread(() => ReceiveLoop(_cancellation.Token))
            {
                IsBackground = true,
                Name = "IndifferentStars.Client.Receive",
            };
            _receiveThread.Start();
            StatusText = $"Contacting {_host}:{_port}…";
        }
        catch (Exception ex)
        {
            State = ConnectionState.Rejected;
            StatusText = $"Transport setup failed: {ex.Message}";
            TransportError?.Invoke(StatusText);
        }
    }

    public void Update(double delta, InputState input)
    {
        LastSnapshotAgeSeconds += delta;
        if (State == ConnectionState.Rejected)
            return;

        if (State == ConnectionState.Connected && LastSnapshotAgeSeconds > 6.0)
        {
            State = ConnectionState.Connecting;
            StatusText = "Connection lost — reconnecting…";
            Welcome = null;
        }

        _lastHelloSeconds += delta;
        if (State != ConnectionState.Connected && _lastHelloSeconds >= 0.6)
        {
            _lastHelloSeconds = 0;
            Send(new HelloMessage(_clientId, _playerName));
        }

        if (State == ConnectionState.Connected)
        {
            _lastInputSeconds += delta;
            if (_lastInputSeconds >= 0.05)
            {
                _lastInputSeconds = 0;
                SendInput(input);
            }
        }

        UpdatePendingRequests();
    }

    public void Poll()
    {
        while (_incoming.TryDequeue(out var message))
        {
            switch (message)
            {
                case HelloAcceptedMessage welcome:
                    Welcome = welcome;
                    State = ConnectionState.Connected;
                    HasEverConnected = true;
                    LastSnapshotAgeSeconds = 0;
                    StatusText = $"Connected — ship #{welcome.PlayerId}";
                    WelcomeReceived?.Invoke(welcome);
                    break;

                case HelloRejectedMessage rejected:
                    State = ConnectionState.Rejected;
                    StatusText = rejected.Reason;
                    JoinRejected?.Invoke(rejected);
                    break;

                case SnapshotMessage snapshot:
                    LastSnapshotTick = snapshot.Tick;
                    LastSnapshotAgeSeconds = 0;
                    if (State != ConnectionState.Connected && Welcome is not null)
                    {
                        State = ConnectionState.Connected;
                        StatusText = "Connected";
                    }
                    SnapshotReceived?.Invoke(snapshot);
                    break;

                case ScanResultMessage scan:
                    if (_pendingScan is { } pending && pending.Id == scan.RequestId)
                        _pendingScan = null;
                    ScanCompleted?.Invoke(scan);
                    break;

                case WarpResultMessage warp:
                    if (_pendingWarp is { } pendingWarp && pendingWarp.Id == warp.RequestId)
                        _pendingWarp = null;
                    WarpCompleted?.Invoke(warp);
                    break;

                case ApproachResultMessage approach:
                    ApproachCompleted?.Invoke(approach);
                    break;

                case DockResultMessage dock:
                    DockCompleted?.Invoke(dock);
                    break;

                case MarketStateMessage market:
                    MarketReceived?.Invoke(market);
                    break;

                case TradeResultMessage trade:
                    if (_pendingTrade is { } pendingTrade && pendingTrade.Id == trade.RequestId)
                        _pendingTrade = null;
                    TradeCompleted?.Invoke(trade);
                    break;

                case PongMessage pong:
                    var sentMs = (long)pong.ClientTimeMs;
                    var nowMs = (long)Time.GetTicksMsec();
                    var rtt = (int)(nowMs - sentMs);
                    PingMs = Math.Clamp(rtt, 0, 9999);
                    break;

                case DisconnectMessage disconnect:
                    State = ConnectionState.Rejected;
                    StatusText = $"Disconnected: {disconnect.Reason}";
                    break;
            }
        }

    }

    private void ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var bytes = _udp!.Receive(ref remote);
                if (ProtocolCodec.TryDecode(bytes, out var message, out _) && message is not null)
                    _incoming.Enqueue(message);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock or SocketError.ConnectionReset)
            {
                // Keepalive loop; a missing server simply produces no packets.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                _incoming.Enqueue(new DisconnectMessage($"network error: {ex.SocketErrorCode}"));
                return;
            }
        }
    }

    private void Send(IProtocolMessage message)
    {
        if (_udp is null)
            return;
        try
        {
            var bytes = ProtocolCodec.Encode(message);
            _udp.Send(bytes, bytes.Length);
        }
        catch (Exception ex)
        {
            StatusText = $"Send failed: {ex.Message}";
            TransportError?.Invoke(StatusText);
        }
    }

    private void SendInput(InputState input)
    {
        _inputSequence++;
        var message = new InputMessage(
            _inputSequence,
            ToSByte(input.Throttle),
            ToSByte(input.Steer),
            ToSByte(input.Pitch),
            ToSByte(input.Vertical),
            input.Boost,
            input.Brake,
            ToSByte(input.Strafe),
            ToSByte(input.Roll),
            input.FlightAssist);
        Send(message);
    }

    private static sbyte ToSByte(float value) =>
        (sbyte)Math.Clamp(Math.Round(value * 127f), -127, 127);

    public void Dispose()
    {
        try
        {
            if (State == ConnectionState.Connected)
                Send(new DisconnectMessage("client quit"));
        }
        catch
        {
            // best effort
        }

        _cancellation?.Cancel();
        _udp?.Dispose();
        _receiveThread?.Join(200);
        _cancellation?.Dispose();
    }
}
