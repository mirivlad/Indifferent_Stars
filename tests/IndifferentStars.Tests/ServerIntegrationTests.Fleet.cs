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
    public async Task AuthoritativeHullReachesTwoIndependentClients()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var hull = HullCatalog.Find("tern")!;
        using var server = new UdpGameServer(new ServerConfig
        {
            BindAddress = "127.0.0.1",
            Port = 0,
            DiagnosticHullId = hull.Id,
        }, catalog, new GameWorld(catalog));
        using var stop = new CancellationTokenSource();
        var run = Task.Factory.StartNew(() => server.Run(stop.Token), stop.Token,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            using var pilot = new UdpClient(AddressFamily.InterNetwork);
            using var observer = new UdpClient(AddressFamily.InterNetwork);
            pilot.Connect(server.LocalEndpoint);
            observer.Connect(server.LocalEndpoint);
            Send(pilot, new HelloMessage(Guid.NewGuid(), "Freight pilot", SurfaceMesh.Fingerprint));
            Send(observer, new HelloMessage(Guid.NewGuid(), "Freight observer", SurfaceMesh.Fingerprint));

            var welcome = Assert.IsType<HelloAcceptedMessage>(WaitFor<HelloAcceptedMessage>(pilot, _ => true));
            var observerWelcome = Assert.IsType<HelloAcceptedMessage>(WaitFor<HelloAcceptedMessage>(observer, _ => true));
            Assert.NotEqual(welcome.PlayerId, observerWelcome.PlayerId);
            Assert.Equal(hull.Id, welcome.HullId);
            Assert.Equal((float)hull.ScanRange, welcome.ScanRange);
            Assert.NotNull(WaitFor<SnapshotMessage>(observer, message =>
                message.Entities.Any(entity => entity.Id == welcome.PlayerId && entity.HullId == hull.Id)));
        }
        finally
        {
            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
