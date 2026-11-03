using System;
using System.Linq;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Server;

public sealed partial class UdpGameServer
{
    private ulong _chatSequence;
    private void HandleChat(PlayerSession sender,ChatSendMessage request,double now)
    {
        sender.LastSeenSeconds=now;
        if(sender.ChatReplies.TryGetValue(request.RequestId,out var cached)){Send(sender.Endpoint,cached);return;}
        ChatError? error=null;
        if(sender.LastChatRequest is { } last && (request.RequestId==last||unchecked(request.RequestId-last)>=0x80000000u))error=ChatError.StaleRequest;
        else if(!RadioChannels.BuiltIn(request.ChannelId))error=ChatError.InvalidChannel;
        else if(!RadioChannels.ValidText(request.Text))error=ChatError.InvalidText;
        else if(now-sender.LastChatAt<.5)error=ChatError.RateLimited;
        IProtocolMessage response;
        if(error is { } reason)response=new ChatRejectedMessage(request.RequestId,request.ChannelId,reason);
        else
        {
            sender.LastChatAt=now;
            var line=new ChatLineMessage(++_chatSequence,request.RequestId,request.ChannelId,sender.PlayerId,sender.Name,
                request.Text.Trim(),DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            response=line;
            foreach(var listener in _sessions.Values)
            {
                if(!RadioChannels.InAudience(request.ChannelId,sender.Ship.Position,listener.Ship.Position))continue;
                if(listener.PendingChat.Count>=64)listener.PendingChat.Remove(listener.PendingChat.Keys.First());
                listener.PendingChat[line.MessageId]=(line,now,now);
                Send(listener.Endpoint,line);
            }
        }
        if(error!=ChatError.StaleRequest)sender.LastChatRequest=request.RequestId;
        if(sender.ChatReplies.Count>=32)sender.ChatReplies.Remove(sender.ChatReplies.Keys.First());
        sender.ChatReplies[request.RequestId]=response;
        if(response is ChatRejectedMessage)Send(sender.Endpoint,response);
    }
    private void RetryChat(double now)
    {
        foreach(var session in _sessions.Values)
        foreach(var (id,pending) in session.PendingChat.ToArray())
        {
            if(now-pending.Started>4){session.PendingChat.Remove(id);continue;}
            if(now-pending.Sent<.4)continue;
            Send(session.Endpoint,pending.Line);session.PendingChat[id]=(pending.Line,pending.Started,now);
        }
    }
}
