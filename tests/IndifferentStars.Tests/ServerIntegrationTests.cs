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

    private static void Send(UdpClient udp, IProtocolMessage message)
    {
        var packet = ProtocolCodec.Encode(message);
        udp.Send(packet, packet.Length);
    }

    private static T? WaitFor<T>(UdpClient udp, Func<T, bool> predicate, double timeoutSeconds = 5)
        where T : class, IProtocolMessage
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
        {
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
