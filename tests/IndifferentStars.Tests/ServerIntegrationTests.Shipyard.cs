using System.Security.Cryptography;
using IndifferentStars.Server;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Tests;

public sealed partial class ServerIntegrationTests
{
    [Fact]
    public async Task ShipyardReceiptsAndStoredHullsSurviveAProcessCrash()
    {
        var directory = Directory.CreateTempSubdirectory("indifferent-shipyard-");
        try
        {
            var catalog = WorldCatalog.Generate(20261101);
            var config = new ServerConfig { BindAddress = "127.0.0.1", Port = 0, SavePath = Path.Combine(directory.FullName, "world.save") };
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var observerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var epoch = Guid.NewGuid();
            var observerEpoch = Guid.NewGuid();
            var purchase = new ShipyardRequestMessage(21, ShipyardAction.BuyHull, 10, ClientId: epoch);
            ShipyardRequestMessage fit;
            uint playerId;
            uint stored;
            int credits;

            await using (var flight = await SaveServerProcess.StartAsync(config, catalog))
            {
                using var pilot = Client(flight.Endpoint);
                using var observer = Client(flight.Endpoint);
                playerId = JoinSaved(pilot, key, epoch).PlayerId;
                Assert.NotEqual(playerId, JoinSaved(observer, observerKey, observerEpoch).PlayerId);
                Send(pilot, new DockRequestMessage((uint)catalog.Station.Id, false));
                Assert.True(WaitFor<DockResultMessage>(pilot, reply => reply.Accepted)?.Accepted);
                uint sequence = 0;
                void KeepAlive()
                {
                    Send(pilot, new InputMessage(++sequence, 0, 0, 0, 0, false, false));
                    Send(observer, new InputMessage(sequence, 0, 0, 0, 0, false, false));
                }
                Assert.NotNull(WaitFor<SnapshotMessage>(pilot, state => state.Entities.Any(ship => ship.Id == playerId && ship.Docked), 55, KeepAlive));
                Send(pilot, purchase);
                var bought = Assert.IsType<ShipyardReplyMessage>(WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 21));
                Assert.Equal(ShipyardError.None, bought.Error);
                Assert.Equal(2, bought.State!.Vessels.Length);
                Assert.Equal(CommodityCatalog.StartingCredits - ShipyardCatalog.HullPrice(10), bought.State.Credits);
                stored = bought.State.ActiveHull;
                var pipit = bought.State.Vessels.Single(ship => ship.HullId == 10);
                var activate = new ShipyardRequestMessage(22, ShipyardAction.ActivateHull, VesselId: pipit.Serial, ClientId: epoch);
                Send(pilot, activate);
                Assert.Equal(ShipyardError.None, WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 22)!.Error);
                Assert.NotNull(WaitFor<SnapshotMessage>(observer, snapshot => snapshot.Entities.Any(ship =>
                    ship.Id == playerId && ship.HullId == 10 && ship.Docked), heartbeat: KeepAlive));
                Send(pilot, new ShipyardRequestMessage(23, ShipyardAction.BuyModule, 4, ClientId: epoch));
                var sensor = Assert.IsType<ShipyardReplyMessage>(WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 23));
                Assert.Equal(ShipyardError.None, sensor.Error);
                credits = sensor.State!.Credits;
                fit = new ShipyardRequestMessage(24, ShipyardAction.FitModule, 4, pipit.Serial, 2, epoch);
                Send(pilot, fit);
                Assert.Equal(ShipyardError.None, WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 24)!.Error);
                // The owned process is killed by disposal before a shutdown save.
            }

            await using (var restart = new RunningSaveServer(config, catalog))
            {
                using var pilot = Client(restart);
                var resumed = JoinSaved(pilot, key, epoch);
                Assert.Equal(playerId, resumed.PlayerId);
                Assert.Equal(10, resumed.HullId);
                Assert.Equal((float)(HullCatalog.Get(10).ScanRange * 1.5), resumed.ScanRange);
                Send(pilot, fit);
                var replay = Assert.IsType<ShipyardReplyMessage>(WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 24));
                Assert.Equal(ShipyardError.None, replay.Error);
                Assert.Equal(credits, replay.State!.Credits);
                Assert.Equal(2, replay.State.Vessels.Length);
                Assert.Equal(4, Assert.Single(replay.State.Vessels.Single(vessel => vessel.HullId == 10).Modules).ModuleId);
                Send(pilot, purchase);
                Assert.Equal(ShipyardError.Stale, WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 21)!.Error);
                Send(pilot, new ShipyardRequestMessage(25, ShipyardAction.SellHull, VesselId: stored, ClientId: epoch));
                var sold = Assert.IsType<ShipyardReplyMessage>(WaitFor<ShipyardReplyMessage>(pilot, result => result.RequestId == 25));
                Assert.Equal(ShipyardError.None, sold.Error);
                Assert.Single(sold.State!.Vessels);
                Assert.True(sold.State.Credits > credits);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
