using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using IndifferentStars.Server;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
using Xunit;

namespace IndifferentStars.Tests;

/// <summary>
/// Exercises the real UDP server over a loopback socket. The tests intentionally use
/// the public protocol only, so a broken codec or handler wiring is caught here rather
/// than in a manual client run.
/// </summary>
public sealed class ServerIntegrationTests
{
    [Fact]
    public void TwoClientsObserveCruiseSafetyAndAuthoritativeWaypoints()
    {
        var catalog = WorldCatalog.Generate(2026);
        var world = new GameWorld(catalog);
        using var server = new UdpGameServer(new ServerConfig { BindAddress = "127.0.0.1", Port = 0 }, catalog, world);
        using var cancellation = new CancellationTokenSource();
        var thread = new Thread(() => server.Run(cancellation.Token)) { IsBackground = true };
        thread.Start();
        try
        {
            using var pilot = new UdpClient(AddressFamily.InterNetwork);
            using var observer = new UdpClient(AddressFamily.InterNetwork);
            pilot.Connect(server.LocalEndpoint); observer.Connect(server.LocalEndpoint);
            Send(pilot, new HelloMessage(Guid.NewGuid(), "Cruise pilot"));
            var welcome = WaitFor<HelloAcceptedMessage>(pilot, _ => true);
            Assert.NotNull(welcome);
            Send(pilot, new CommsRequestMessage((uint)catalog.Station.Id));
            var comms = WaitFor<CommsResultMessage>(pilot, reply => reply.Success);
            Assert.NotNull(comms);
            Assert.Contains("диспетчер", comms!.RussianMessage);
            Send(observer, new HelloMessage(Guid.NewGuid(), "Observer"));
            Assert.NotNull(WaitFor<HelloAcceptedMessage>(observer, _ => true));
            Send(pilot, new InputMessage(1, 127, 0, 0, 0, false, false, Cruise: true));
            var snapshot = WaitFor<SnapshotMessage>(observer, message =>
                System.Array.Exists(message.Entities, entity => entity.Id == welcome!.PlayerId && entity.CruiseInhibited));
            Assert.NotNull(snapshot);
            var ship = Assert.Single(snapshot!.Entities, entity => entity.Id == welcome!.PlayerId);
            Assert.False(ship.Cruising);
            Assert.False(ship.CruiseBraking);
            Assert.True(ship.Velocity.Length > 0);
            Send(pilot, new InputMessage(2, 127, 0, 0, 0, true, false));
            Assert.NotNull(WaitFor<SnapshotMessage>(observer, message => System.Array.Exists(message.Entities,
                entity => entity.Id == welcome!.PlayerId && !entity.CruiseInhibited && !entity.Cruising && entity.Velocity.Length > 0)));
            Send(pilot, new WarpRequestMessage(12, (uint)catalog.Planet.Id, 10000));
            var rejected = WaitFor<WarpResultMessage>(pilot, message => message.RequestId == 12);
            Assert.NotNull(rejected);
            Assert.False(rejected!.Success);
            Assert.Contains("cruise", rejected.Message);
            var destination = new IndifferentStars.Shared.Vec3d(-30_000, 260, 0);
            Send(pilot, new ApproachRequestMessage(0, Waypoint: destination));
            Assert.True(WaitFor<ApproachResultMessage>(pilot, message => message.TargetId == 0)?.Accepted);
            Assert.NotNull(WaitFor<SnapshotMessage>(observer, message => System.Array.Exists(message.Entities,
                entity => entity.Id == welcome!.PlayerId && entity.ApproachPoint is { } point && point.Equals(destination))));
            Send(pilot, new ApproachRequestMessage(0, Waypoint: new IndifferentStars.Shared.Vec3d(double.NaN, 0, 0)));
            Assert.False(WaitFor<ApproachResultMessage>(pilot, message => message.TargetId == 0)?.Accepted);
        }
        finally
        {
            cancellation.Cancel();
            Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public void MarketAndTradeRequestsAreAuthoritativeAndIdempotent()
    {
        var catalog = WorldCatalog.Generate(2026);
        var world = new GameWorld(catalog);
        var config = new ServerConfig { BindAddress = "127.0.0.1", Port = 0 };
        using var server = new UdpGameServer(config, catalog, world);
        using var cancellation = new CancellationTokenSource();
        var thread = new Thread(() => server.Run(cancellation.Token)) { IsBackground = true };
        thread.Start();

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Connect(server.LocalEndpoint);

            Send(udp, new HelloMessage(Guid.NewGuid(), "Integration Trader"));
            Assert.NotNull(WaitFor<HelloAcceptedMessage>(udp, _ => true));
            var snapshot = WaitFor<SnapshotMessage>(udp, message => message.Tick > 0);
            Assert.NotNull(snapshot);
            Assert.Equal((ulong)Math.Round(snapshot!.Tick * 1000.0 / config.TickRate), snapshot.ServerTimeMs);

            Send(udp, new MarketRequestMessage(41));
            var market = WaitFor<MarketStateMessage>(udp, message => message.RequestId == 41);
            Assert.NotNull(market);
            Assert.Equal(CommodityCatalog.StartingCredits, market!.State.Credits);
            Assert.Equal(CommodityCatalog.All.Count, market.State.Entries.Length);

            Send(udp, new TradeRequestMessage(42, 1, 1, true));
            var rejected = WaitFor<TradeResultMessage>(udp, message => message.RequestId == 42);
            Assert.NotNull(rejected);
            Assert.False(rejected!.Success);
            Assert.Contains("Dock", rejected.Message);
            Assert.Contains("пристыкуйтесь", rejected.RussianMessage);

            Send(udp, new TradeRequestMessage(42, 1, 1, true));
            var repeated = WaitFor<TradeResultMessage>(udp, message => message.RequestId == 42);
            Assert.NotNull(repeated);
            Assert.Equal(rejected.Success, repeated!.Success);
            Assert.Equal(rejected.Message, repeated.Message);
            Assert.Equal(rejected.State.Credits, repeated.State.Credits);
        }
        finally
        {
            cancellation.Cancel();
            Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
            server.Stop();
        }
    }

    [Fact]
    public void ObserverSeesDockingAndCollision()
    {
        var catalog = WorldCatalog.Generate(2026);
        var world = new GameWorld(catalog);
        using var server = new UdpGameServer(new ServerConfig { BindAddress = "127.0.0.1", Port = 0 }, catalog, world);
        using var cancellation = new CancellationTokenSource();
        var thread = new Thread(() => server.Run(cancellation.Token)) { IsBackground = true };
        thread.Start();
        try
        {
            using var pilot = new UdpClient(AddressFamily.InterNetwork);
            using var observer = new UdpClient(AddressFamily.InterNetwork);
            pilot.Connect(server.LocalEndpoint); observer.Connect(server.LocalEndpoint);
            Send(pilot, new HelloMessage(Guid.NewGuid(), "Dock pilot"));
            var welcome = WaitFor<HelloAcceptedMessage>(pilot, _ => true);
            Assert.NotNull(welcome);
            Send(observer, new HelloMessage(Guid.NewGuid(), "Observer"));
            Assert.NotNull(WaitFor<HelloAcceptedMessage>(observer, _ => true));
            Send(pilot, new DockRequestMessage((uint)catalog.Station.Id, false));
            Assert.True(WaitFor<DockResultMessage>(pilot, reply => reply.Accepted)?.Accepted);
            uint sequence = 1;
            var docked = WaitFor<SnapshotMessage>(observer, message => Array.Exists(message.Entities,
                entity => entity.Id == welcome!.PlayerId && entity.Docked), 30, () =>
                {
                    while (pilot.Available > 0)
                    {
                        IPEndPoint sender = new(IPAddress.Any, 0);
                        pilot.Receive(ref sender);
                    }
                    Send(pilot, new InputMessage(sequence++, 0, 0, 0, 0, false, false));
                    Send(observer, new InputMessage(sequence++, 0, 0, 0, 0, false, false));
                });
            Assert.NotNull(docked);
            Send(pilot, new DockRequestMessage((uint)catalog.Station.Id, true));
            Assert.True(WaitFor<DockResultMessage>(pilot, reply => reply.Undock && reply.Accepted)?.Accepted);
            var dockedShip = Array.Find(docked!.Entities, entity => entity.Id == welcome!.PlayerId)!;
            var behindStation = catalog.Station.Position -
                (dockedShip.Position - catalog.Station.Position).Normalized() * 500;
            Send(pilot, new ApproachRequestMessage(0, Waypoint: behindStation));
            Assert.True(WaitFor<ApproachResultMessage>(pilot, reply => reply.Accepted)?.Accepted);
            var until = Stopwatch.StartNew();
            SnapshotMessage? impact = null;
            EntitySnapshot? last = null;
            while (until.Elapsed.TotalSeconds < 12 && impact is null)
            {
                Send(observer, new InputMessage(sequence++, 0, 0, 0, 0, false, false));
                Send(pilot, new InputMessage(sequence++, 0, 0, 0, 0, false, false));
                impact = WaitFor<SnapshotMessage>(observer, message =>
                {
                    last = Array.Find(message.Entities, entity => entity.Id == welcome!.PlayerId);
                    return last is { } entity && !entity.Docked && entity.Velocity.Length < 0.01 &&
                        IndifferentStars.Shared.Vec3d.Distance(entity.Position, catalog.Station.Position) < 180;
                }, 0.1);
            }
            Assert.True(impact is not null, $"Last observer state: {last}");
        }
        finally
        {
            cancellation.Cancel();
            Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
        }
    }

    private static void Send(UdpClient udp, IProtocolMessage message)
    {
        var packet = ProtocolCodec.Encode(message);
        udp.Send(packet, packet.Length);
    }

    private static T? WaitFor<T>(UdpClient udp, Func<T, bool> predicate, double timeoutSeconds = 5, Action? heartbeat = null)
        where T : class, IProtocolMessage
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            heartbeat?.Invoke();
            if (!udp.Client.Poll(100_000, SelectMode.SelectRead))
                continue;

            IPEndPoint remote = new(IPAddress.Any, 0);
            var data = udp.Receive(ref remote);
            if (!ProtocolCodec.TryDecode(data, out var decoded, out _) || decoded is not T typed)
                continue;
            if (predicate(typed))
                return typed;
        }

        return null;
    }
}
