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
    public void TwoClientsReceiveTheSameSimulationEventAndNpcActivityState()
    {
        var catalog=WorldCatalog.Generate(20261101);var world=new GameWorld(catalog);
        var survey=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Surveyor);
        survey.Position=survey.Work!.Goal;survey.RouteActive=false;survey.Work.Phase=NpcPhase.Working;survey.Work.Until=0;
        world.Tick(.05);
        using var server=new UdpGameServer(new ServerConfig {BindAddress="127.0.0.1",Port=0},catalog,world);
        using var cancellation=new CancellationTokenSource();
        using var a=new UdpClient(AddressFamily.InterNetwork);using var b=new UdpClient(AddressFamily.InterNetwork);
        a.Connect(server.LocalEndpoint);b.Connect(server.LocalEndpoint);
        Send(a,new HelloMessage(Guid.NewGuid(),"Activity pilot",SurfaceMesh.Fingerprint));
        Send(b,new HelloMessage(Guid.NewGuid(),"Activity observer",SurfaceMesh.Fingerprint));
        var thread=new Thread(()=>server.Run(cancellation.Token)){IsBackground=true};thread.Start();
        try
        {
            Assert.NotNull(WaitFor<HelloAcceptedMessage>(a,_=>true));Assert.NotNull(WaitFor<HelloAcceptedMessage>(b,_=>true));
            var first=WaitFor<ChatLineMessage>(a,m=>m.SenderId==survey.Id&&m.ChannelId==RadioChannels.System);
            var second=WaitFor<ChatLineMessage>(b,m=>m.SenderId==survey.Id&&m.ChannelId==RadioChannels.System);
            Assert.NotNull(first);Assert.NotNull(second);Assert.Equal(first,second);
            Assert.NotNull(first.SignalPosition);Assert.Contains("Исследование",first.RussianText);
            Send(a,new ChatAckMessage(first.MessageId));Send(b,new ChatAckMessage(second.MessageId));
            var observed=WaitFor<SnapshotMessage>(b,s=>s.Entities.Any(e=>e.Activity?.Occupation==NpcOccupation.Patrol));
            Assert.NotNull(observed);var npc=observed.Entities.First(e=>e.Activity?.Occupation==NpcOccupation.Patrol);
            Assert.NotNull(WaitFor<SnapshotMessage>(a,s=>s.Entities.Any(e=>e.Id==npc.Id&&e.Activity?.Occupation==NpcOccupation.Patrol)));
        }
        finally{cancellation.Cancel();Assert.True(thread.Join(TimeSpan.FromSeconds(3)));}
    }
}
