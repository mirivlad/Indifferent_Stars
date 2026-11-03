using System;
using System.Net.Sockets;
using System.Threading;
using IndifferentStars.Server;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed partial class ServerIntegrationTests
{
    [Fact]
    public void ChatRoutesAuthoritativeMessagesRetriesAndRejectsDuplicatesAndSpam()
    {
        var catalog=WorldCatalog.Generate(2026);var world=new GameWorld(catalog);
        using var server=new UdpGameServer(new ServerConfig {BindAddress="127.0.0.1",Port=0},catalog,world);
        using var cancellation=new CancellationTokenSource();
        var thread=new Thread(()=>server.Run(cancellation.Token)){IsBackground=true};thread.Start();
        try
        {
            using var a=new UdpClient(AddressFamily.InterNetwork);using var b=new UdpClient(AddressFamily.InterNetwork);
            a.Connect(server.LocalEndpoint);b.Connect(server.LocalEndpoint);
            Send(a,new HelloMessage(Guid.NewGuid(),"Author",SurfaceMesh.Fingerprint));
            var author=WaitFor<HelloAcceptedMessage>(a,_=>true);Assert.NotNull(author);
            Send(b,new HelloMessage(Guid.NewGuid(),"Reader",SurfaceMesh.Fingerprint));Assert.NotNull(WaitFor<HelloAcceptedMessage>(b,_=>true));
            var request=new ChatSendMessage(1,RadioChannels.Local,"Привет [b]пилоты[/b]");Send(a,request);
            var line=WaitFor<ChatLineMessage>(b,m=>m.SenderId==author.PlayerId&&m.RequestId==1);Assert.NotNull(line);Assert.Equal(request.Text,line.Text);
            Assert.Equal(author.PlayerId,line.SenderId);Assert.Equal("Author",line.Sender);Assert.Equal(request.ChannelId,line.ChannelId);
            var own=WaitFor<ChatLineMessage>(a,m=>m.SenderId==author.PlayerId&&m.RequestId==1);Assert.Equal(line,own);
            Send(a,new ChatAckMessage(line.MessageId));Send(b,new ChatAckMessage(line.MessageId));
            Send(a,new ChatSendMessage(2,RadioChannels.General,"too soon"));
            Assert.Equal(ChatError.RateLimited,WaitFor<ChatRejectedMessage>(a,r=>r.RequestId==2)?.Error);
            Send(a,request);Assert.Equal(line,WaitFor<ChatLineMessage>(a,m=>m.RequestId==1));
            Assert.Null(WaitFor<ChatLineMessage>(b,m=>m.SenderId==author.PlayerId&&m.MessageId!=line.MessageId,.65));
            Send(a,new ChatSendMessage(3,RadioChannels.System,"system"));
            var system=WaitFor<ChatLineMessage>(b,m=>m.ChannelId==RadioChannels.System&&m.SenderId==author.PlayerId);Assert.NotNull(system);
            // No ACK: the same delivery must be retried, without a new message id.
            Assert.Equal(system,WaitFor<ChatLineMessage>(b,m=>m.MessageId==system.MessageId,2));
            Send(b,new ChatAckMessage(system.MessageId));
            Send(a,new ChatSendMessage(4,99,"unknown"));
            Assert.Equal(ChatError.InvalidChannel,WaitFor<ChatRejectedMessage>(a,r=>r.RequestId==4)?.Error);
            Send(a,new ChatSendMessage(5,RadioChannels.Local,"two\nlines"));
            Assert.Equal(ChatError.InvalidText,WaitFor<ChatRejectedMessage>(a,r=>r.RequestId==5)?.Error);
            Thread.Sleep(550);Send(a,new ChatSendMessage(6,RadioChannels.General,"general"));
            Assert.NotNull(WaitFor<ChatLineMessage>(b,m=>m.ChannelId==RadioChannels.General));
        }
        finally{cancellation.Cancel();Assert.True(thread.Join(TimeSpan.FromSeconds(3)));}
    }
}
