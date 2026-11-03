using System.Net.Sockets;
using IndifferentStars.Server;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed partial class ServerIntegrationTests
{
    [Fact]
    public void TwoClientsObserveOrbitTransferCancellationAndServerDistanceLimits()
    {
        var catalog = WorldCatalog.Generate(2026);
        using var server = new UdpGameServer(new ServerConfig { BindAddress = "127.0.0.1", Port = 0 }, catalog, new GameWorld(catalog));
        using var cancellation = new CancellationTokenSource();
        var thread = new Thread(() => server.Run(cancellation.Token)) { IsBackground = true }; thread.Start();
        try
        {
            using var pilot = new UdpClient(AddressFamily.InterNetwork); using var observer = new UdpClient(AddressFamily.InterNetwork);
            pilot.Connect(server.LocalEndpoint); observer.Connect(server.LocalEndpoint);
            Send(pilot, new HelloMessage(Guid.NewGuid(), "Orbital pilot", SurfaceMesh.Fingerprint));
            var joined = WaitFor<HelloAcceptedMessage>(pilot, _ => true); Assert.NotNull(joined);
            Send(observer, new HelloMessage(Guid.NewGuid(), "Orbital observer", SurfaceMesh.Fingerprint));
            Assert.NotNull(WaitFor<HelloAcceptedMessage>(observer, _ => true));
            var star = (uint)catalog.Star.Id; var planet = (uint)catalog.Planet.Id;
            var unsafeWarp = new WarpRequestMessage(1, star, SurfaceDistance: 500);
            Send(pilot, unsafeWarp);
            var warp = WaitFor<WarpResultMessage>(pilot, r => r.RequestId == 1); Assert.NotNull(warp); Assert.False(warp.Success);
            Assert.Contains("1000000", warp.Message);
            var unsafeOrbit = new OrbitRequestMessage(2, planet, 500);
            Send(pilot, unsafeOrbit);
            var rejected = WaitFor<OrbitResultMessage>(pilot, r => r.RequestId == 2); Assert.NotNull(rejected);
            Assert.Equal(OrbitError.UnsafeAltitude, rejected.Error);
            var request = new OrbitRequestMessage(3, planet, 500000, true);
            Send(pilot, request);
            Assert.True(WaitFor<OrbitResultMessage>(pilot, r => r.RequestId == 3, heartbeat: () => Send(pilot, request))?.Accepted);
            Send(pilot, request with { Altitude = 600000, Retrograde = false });
            Assert.True(WaitFor<OrbitResultMessage>(pilot, r => r.RequestId == 3)?.Accepted);
            EntitySnapshot? observed = null;
            Assert.NotNull(WaitFor<SnapshotMessage>(observer, s =>
            {
                observed = s.Entities.FirstOrDefault(e => e.Id == joined.PlayerId && e.OrbitTargetId == planet && e.RouteActive);
                return observed is not null;
            }));
            Assert.Equal(500000, observed!.OrbitAltitude); Assert.True(observed.OrbitRetrograde); Assert.False(observed.OrbitEstablished);
            Assert.NotNull(WaitFor<SnapshotMessage>(pilot, s => s.Entities.Any(e => e.Id == joined.PlayerId && e.OrbitTargetId == planet && e.OrbitAltitude == 500000 && e.OrbitRetrograde)));
            Send(pilot, unsafeOrbit);
            Assert.Equal(OrbitError.Stale, WaitFor<OrbitResultMessage>(pilot, r => r.RequestId == 2)?.Error);
            Send(pilot, new RouteRequestMessage(4, Array.Empty<NavigationPoint>(), false));
            Assert.True(WaitFor<RouteResultMessage>(pilot, r => r.RequestId == 4)?.Accepted);
            Assert.NotNull(WaitFor<SnapshotMessage>(observer, s => s.Entities.Any(e => e.Id == joined.PlayerId && e.OrbitTargetId == 0 && !e.RouteActive)));
        }
        finally { cancellation.Cancel(); Assert.True(thread.Join(TimeSpan.FromSeconds(3))); }
    }
}
