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
    public void TwoClientsReconstructTheSameNonDefaultSeededSystem()
    {
        var catalog=WorldCatalog.Generate(81472,7);
        using var server=new UdpGameServer(new ServerConfig {BindAddress="127.0.0.1",Port=0},catalog,new GameWorld(catalog));
        using var cancellation=new CancellationTokenSource();var thread=new Thread(()=>server.Run(cancellation.Token)){IsBackground=true};thread.Start();
        try
        {
            using var a=new UdpClient(AddressFamily.InterNetwork);using var b=new UdpClient(AddressFamily.InterNetwork);
            a.Connect(server.LocalEndpoint);b.Connect(server.LocalEndpoint);
            Send(a,new HelloMessage(Guid.NewGuid(),"Explorer",SurfaceMesh.Fingerprint));
            Send(b,new HelloMessage(Guid.NewGuid(),"Observer",SurfaceMesh.Fingerprint));
            var first=WaitFor<HelloAcceptedMessage>(a,_=>true);var second=WaitFor<HelloAcceptedMessage>(b,_=>true);
            Assert.NotNull(first);Assert.NotNull(second);Assert.Equal(7,first.SystemIndex);Assert.Equal(first.SystemIndex,second.SystemIndex);
            var client=WorldCatalog.Generate(first.WorldSeed,first.SystemIndex);
            Assert.Equal(catalog.SystemName,client.SystemName);Assert.Equal(client.SystemName,first.SystemName);
            Assert.Equal(catalog.Celestials.Select(c=>c.SystemPosition),client.Celestials.Select(c=>c.SystemPosition));
            Assert.NotNull(WaitFor<SnapshotMessage>(b,s=>s.Entities.Any(e=>e.Id==first.PlayerId)));
        }
        finally{cancellation.Cancel();Assert.True(thread.Join(TimeSpan.FromSeconds(3)));}
    }
}
