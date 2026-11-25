using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using IndifferentStars.Server;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed partial class ServerIntegrationTests
{
    [Fact]
    public async Task SavedProfileResumesDockAndCommitsTradeOnlyOnce()
    {
        var directory = Directory.CreateTempSubdirectory("indifferent-save-");
        try
        {
            var catalog = WorldCatalog.Generate(2026);
            var config = new ServerConfig { BindAddress = "127.0.0.1", Port = 0, SavePath = Path.Combine(directory.FullName, "world.save") };
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var epoch = Guid.NewGuid();
            var trade = new TradeRequestMessage(40, 1, 2, true, epoch);
            uint playerId;
            TradeResultMessage purchase;

            var observerEpoch = Guid.NewGuid();
            uint observerId;
            Vec3d observerStart;
            await using (var flight = await SaveServerProcess.StartAsync(config, catalog))
            {
                using var pilot = Client(flight.Endpoint);
                using var observer = Client(flight.Endpoint);
                playerId = JoinSaved(pilot, key, epoch, observer).PlayerId;
                var other = JoinSaved(observer, otherKey, observerEpoch);
                observerId = other.PlayerId;
                observerStart = other.Spawn;
                Assert.NotEqual(playerId, other.PlayerId);
                Assert.Equal("Same callsign", other.PlayerName);
                Send(pilot, new DockRequestMessage((uint)catalog.Station.Id, false));
                Assert.True(WaitFor<DockResultMessage>(pilot, reply => reply.Accepted)?.Accepted);
                uint sequence = 0;
                Assert.NotNull(WaitFor<SnapshotMessage>(pilot, state => state.Entities.Any(ship => ship.Id == playerId && ship.Docked),
                    40, () => Send(pilot, new InputMessage(++sequence, 0, 0, 0, 0, false, false))));
                Send(pilot, trade);
                purchase = Assert.IsType<TradeResultMessage>(WaitFor<TradeResultMessage>(pilot, result => result.RequestId == 40));
                Assert.True(purchase.Success);
                Assert.Equal(2, purchase.State.CargoUsed);
                Assert.Equal(CommodityCatalog.StartingCredits - 2 * CommodityCatalog.Find(1)!.BuyPrice, purchase.State.Credits);

                // The observer may have timed out while the pilot docked.
                Assert.Equal(observerId, JoinSaved(observer, otherKey, observerEpoch).PlayerId);
                uint observerSequence = 0;
                Assert.NotNull(WaitFor<SnapshotMessage>(observer, state =>
                    state.Entities.Any(ship => ship.Id == observerId && ship.Velocity.Length > 20), 8,
                    () => Send(observer, new InputMessage(++observerSequence, -110, 0, 0, 0, false, false, Roll: 30))));
                Send(observer, new TradeRequestMessage(1, 1, 1, true, observerEpoch));
                Assert.False(WaitFor<TradeResultMessage>(observer, result => result.RequestId == 1)?.Success);
                // Disposal kills this owned process without running a shutdown save.
            }

            await using (var restart = new RunningSaveServer(config, catalog))
            {
                using var pilot = Client(restart);
                var welcome = JoinSaved(pilot, key, epoch);
                Assert.Equal(playerId, welcome.PlayerId);
                Assert.NotNull(WaitFor<SnapshotMessage>(pilot, state => state.Entities.Any(ship => ship.Id == playerId && ship.Docked)));
                Send(pilot, trade);
                var replay = Assert.IsType<TradeResultMessage>(WaitFor<TradeResultMessage>(pilot, result => result.RequestId == 40));
                Assert.True(replay.Success);
                Assert.Equal(purchase.State.Credits, replay.State.Credits);
                Assert.Equal(purchase.State.CargoUsed, replay.State.CargoUsed);
                Assert.Equal(purchase.State.Entries, replay.State.Entries);
                Send(pilot, trade with { RequestId = 39 });
                Assert.False(WaitFor<TradeResultMessage>(pilot, result => result.RequestId == 39)?.Success);
                using var observer = Client(restart);
                var resumed = JoinSaved(observer, otherKey, observerEpoch);
                Assert.Equal(observerId, resumed.PlayerId);
                Assert.True(Vec3d.Distance(observerStart, resumed.Spawn) > 1);
                Assert.NotNull(WaitFor<SnapshotMessage>(observer, state => state.Entities.Any(ship =>
                    ship.Id == observerId && ship.Velocity.Length > 10 && Math.Abs(ship.Orientation.Z) > .01)));
                Send(pilot, new DisconnectMessage("restart client"));
            }

            await using (var restart = new RunningSaveServer(config, catalog))
            {
                using var pilot = Client(restart);
                var newEpoch = Guid.NewGuid();
                Assert.Equal(playerId, JoinSaved(pilot, key, newEpoch).PlayerId);
                Send(pilot, trade);
                Send(pilot, new MarketRequestMessage(1));
                var current = Assert.IsType<MarketStateMessage>(WaitFor<MarketStateMessage>(pilot, result => result.RequestId == 1));
                Assert.Equal(purchase.State.Credits, current.State.Credits);
                Assert.Equal(purchase.State.CargoUsed, current.State.CargoUsed);
                Send(pilot, trade with { ClientId = newEpoch, RequestId = 2, Quantity = 1, Buy = false });
                var sale = Assert.IsType<TradeResultMessage>(WaitFor<TradeResultMessage>(pilot, result => result.RequestId == 2));
                Assert.True(sale.Success);
                Assert.Equal(1, sale.State.CargoUsed);
                Assert.Equal(purchase.State.Credits + CommodityCatalog.Find(1)!.SellPrice, sale.State.Credits);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void BackupRestoresNpcFlightAndRejectsDifferentWorld()
    {
        var directory = Directory.CreateTempSubdirectory("indifferent-save-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.save");
            var catalog = WorldCatalog.Generate(20261101);
            var config = new ServerConfig { BindAddress = "127.0.0.1", Port = 0, SavePath = path };
            var original = new GameWorld(catalog);
            for (var i = 0; i < 210; i++) original.Tick(.05);
            var before = original.CaptureSimulation();
            using var stopped = new CancellationTokenSource();
            stopped.Cancel();
            using (var server = new UdpGameServer(config, catalog, original))
            {
                Assert.Throws<IOException>(() => new UdpGameServer(config, catalog, new GameWorld(catalog)));
                server.Run(stopped.Token);
            }

            File.WriteAllText(path, "incomplete write");
            var restored = new GameWorld(catalog);
            using (var server = new UdpGameServer(config, catalog, restored))
            {
                var after = restored.CaptureSimulation();
                Assert.Equal(before.Tick, after.Tick);
                Assert.Equal(before.TimeSeconds, after.TimeSeconds);
                Assert.Equal(before.ActivitySequence, after.ActivitySequence);
                Assert.Equal(before.RecentActivities, after.RecentActivities);
                Assert.Equal(before.Deposits.OrderBy(p => p.Key), after.Deposits.OrderBy(p => p.Key));
                Assert.Equal(before.Npcs.Select(ship => ship.Orientation), after.Npcs.Select(ship => ship.Orientation));

                for (var i = 0; i < 50; i++) { original.Tick(.05); restored.Tick(.05); }
                Assert.Equal(original.Npcs.Select(ship => ship.Position), restored.Npcs.Select(ship => ship.Position));
                Assert.Equal(original.Npcs.Select(ship => ship.Velocity), restored.Npcs.Select(ship => ship.Velocity));
                Assert.Equal(original.Npcs.Select(ship => ship.Work!.Phase), restored.Npcs.Select(ship => ship.Work!.Phase));
                server.Run(stopped.Token);
            }

            var wrong = WorldCatalog.Generate(20261102);
            Assert.Throws<InvalidOperationException>(() => new UdpGameServer(config, wrong, new GameWorld(wrong)));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static HelloAcceptedMessage JoinSaved(UdpClient udp, ECDsa key, Guid epoch, UdpClient? replayFrom = null)
    {
        var hello = new HelloMessage(epoch, "Same callsign", SurfaceMesh.Fingerprint,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        Send(udp, hello);
        var reply = WaitFor<IProtocolMessage>(udp, message => message is ProfileChallengeMessage or HelloAcceptedMessage);
        if (reply is HelloAcceptedMessage present) return present;
        var challenge = Assert.IsType<ProfileChallengeMessage>(reply);
        var signed = hello with { Proof = challenge.Challenge + "." + Convert.ToBase64String(
            key.SignData(Encoding.ASCII.GetBytes(challenge.Challenge), HashAlgorithmName.SHA256)) };
        if (replayFrom is not null)
        {
            Send(replayFrom, signed);
            var different = Assert.IsType<ProfileChallengeMessage>(WaitFor<ProfileChallengeMessage>(replayFrom, _ => true));
            Assert.NotEqual(challenge.Challenge, different.Challenge);
        }
        Send(udp, signed);
        return Assert.IsType<HelloAcceptedMessage>(WaitFor<HelloAcceptedMessage>(udp, _ => true));
    }

    private static UdpClient Client(RunningSaveServer running) => Client(running.Server.LocalEndpoint);

    private static UdpClient Client(IPEndPoint endpoint)
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(endpoint);
        return udp;
    }

    private sealed class SaveServerProcess(Process process, IPEndPoint endpoint) : IAsyncDisposable
    {
        public IPEndPoint Endpoint { get; } = endpoint;

        public static async Task<SaveServerProcess> StartAsync(ServerConfig config, WorldCatalog catalog)
        {
            int port;
            using (var available = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                port = ((IPEndPoint)available.Client.LocalEndPoint!).Port;

            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { typeof(ServerConfig).Assembly.Location, "--bind", config.BindAddress,
                "--port", port.ToString(), "--seed", catalog.Seed.ToString(), "--save", config.SavePath! })
                start.ArgumentList.Add(argument);
            var process = Process.Start(start)!;
            var running = new SaveServerProcess(process, new IPEndPoint(IPAddress.Loopback, port));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                    if (line.Contains("Listening on udp", StringComparison.Ordinal)) return running;
                throw new InvalidOperationException(await process.StandardError.ReadToEndAsync(timeout.Token));
            }
            catch
            {
                await running.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { process.Dispose(); }
        }
    }

    private sealed class RunningSaveServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        public UdpGameServer Server { get; }

        public RunningSaveServer(ServerConfig config, WorldCatalog catalog)
        {
            Server = new UdpGameServer(config, catalog, new GameWorld(catalog));
            _run = Task.Factory.StartNew(() => Server.Run(_stop.Token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _run.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { Server.Dispose(); _stop.Dispose(); }
        }
    }
}
