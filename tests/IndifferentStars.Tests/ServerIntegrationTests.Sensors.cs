using System.Net.Sockets;
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
    public void TwoClientsReceiveSharedDistantContactsButOnlyLocalDetailedSnapshots()
    {
        var catalog=WorldCatalog.Generate(20261101); var world=new GameWorld(catalog);
        var distant=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Surveyor);
        using var server=new UdpGameServer(new ServerConfig {BindAddress="127.0.0.1",Port=0},catalog,world);
        using var cancellation=new CancellationTokenSource();
        using var a=new UdpClient(AddressFamily.InterNetwork); using var b=new UdpClient(AddressFamily.InterNetwork);
        a.Connect(server.LocalEndpoint); b.Connect(server.LocalEndpoint);
        Send(a,new HelloMessage(Guid.NewGuid(),"Sensor pilot",SurfaceMesh.Fingerprint));
        Send(b,new HelloMessage(Guid.NewGuid(),"Sensor observer",SurfaceMesh.Fingerprint));
        var thread=new Thread(()=>server.Run(cancellation.Token)){IsBackground=true};thread.Start();
        try
        {
            Assert.NotNull(WaitFor<HelloAcceptedMessage>(a,_=>true)); Assert.NotNull(WaitFor<HelloAcceptedMessage>(b,_=>true));
            // Surveyors may still be departing their home; the contact channel is independent of near replication.
            var first=WaitFor<SensorContactsMessage>(a,m=>m.Contacts.Any(c=>c.Id==distant.Id));
            var second=WaitFor<SensorContactsMessage>(b,m=>m.Contacts.Any(c=>c.Id==distant.Id));
            Assert.NotNull(first); Assert.NotNull(second);
            var ca=first.Contacts.Single(c=>c.Id==distant.Id); var cb=second.Contacts.Single(c=>c.Id==distant.Id);
            Assert.Equal(ca.Name,cb.Name); Assert.Equal(ContactSource.Transponder,ca.Source); Assert.Equal(ca.DestinationId,cb.DestinationId);
            var snapshot=WaitFor<SnapshotMessage>(a,_=>true); Assert.NotNull(snapshot);
            Assert.DoesNotContain(snapshot.Entities,e=>e.Id==distant.Id);
            Send(a,new TransponderMessage(1,false));
            Assert.False(WaitFor<TransponderMessage>(a,m=>m.Sequence==1)!.Enabled);
            Send(a,new TransponderMessage(1,true));
            Assert.False(WaitFor<TransponderMessage>(a,m=>m.Sequence==1)!.Enabled);
            var caller=world.Players.Values.Single(p=>p.Name=="Sensor pilot");
            Assert.NotNull(WaitFor<SensorContactsMessage>(b,m=>m.Contacts.Any(c=>c.Id==caller.Id&&!c.Transponder&&c.Source==ContactSource.Tactical)));
        }
        finally {cancellation.Cancel(); Assert.True(thread.Join(TimeSpan.FromSeconds(3)));}
    }
}
