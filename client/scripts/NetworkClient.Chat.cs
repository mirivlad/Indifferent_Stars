using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class NetworkClient
{
    private readonly Dictionary<uint,(ChatSendMessage Request,double Started,double Sent)> _pendingChat=new();
    private readonly HashSet<ulong> _chatSeen=new();
    private readonly Queue<ulong> _chatOrder=new();
    public event Action<ChatLineMessage>? ChatReceived;
    public event Action<ChatRejectedMessage>? ChatRejected;
    public event Action<ushort>? ChatTimedOut;
    public bool SendChat(ushort channel,string text)
    {
        if(State!=ConnectionState.Connected||_pendingChat.Count>=16)return false;
        var request=new ChatSendMessage(_requestSequence++,channel,text);
        var now=NowSeconds();_pendingChat[request.RequestId]=(request,now,now);Send(request);return true;
    }
    private void ReceiveChat(ChatLineMessage line)
    {
        Send(new ChatAckMessage(line.MessageId));
        if(line.SenderId==PlayerId)_pendingChat.Remove(line.RequestId);
        if(!_chatSeen.Add(line.MessageId))return;
        _chatOrder.Enqueue(line.MessageId);
        if(_chatOrder.Count>512)_chatSeen.Remove(_chatOrder.Dequeue());
        ChatReceived?.Invoke(line);
    }
    private void RetryChat()
    {
        var now=NowSeconds();
        foreach(var (id,pending) in _pendingChat.ToArray())
        {
            if(now-pending.Started>4){_pendingChat.Remove(id);ChatTimedOut?.Invoke(pending.Request.ChannelId);continue;}
            if(now-pending.Sent<.5)continue;
            Send(pending.Request);_pendingChat[id]=(pending.Request,pending.Started,now);
        }
    }
}
