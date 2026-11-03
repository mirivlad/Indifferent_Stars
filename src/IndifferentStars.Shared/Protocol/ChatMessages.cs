using System;
using System.Linq;
using System.Text;

namespace IndifferentStars.Shared.Protocol;

public static class RadioChannels
{
    public const ushort Local=1, System=2, General=3;
    public const int MaxTextBytes=512;
    public static bool BuiltIn(ushort id)=>id is Local or System or General;
    public static bool InAudience(ushort channel,Vec3d sender,Vec3d receiver)=>BuiltIn(channel)&&
        (channel!=Local||Vec3d.Distance(sender,receiver)<=IndifferentStars.Shared.World.WorldCatalog.RelevanceRange);
    public static bool ValidText(string text)=>!string.IsNullOrWhiteSpace(text)&&Encoding.UTF8.GetByteCount(text)<=MaxTextBytes&&
        !text.Any(c=>char.IsControl(c)||c is '\u2028' or '\u2029');
}
public enum ChatError : byte { InvalidChannel=1, InvalidText=2, RateLimited=3, StaleRequest=4 }
public sealed record ChatSendMessage(uint RequestId,ushort ChannelId,string Text):IProtocolMessage {public MessageType Type=>MessageType.ChatSend;}
public sealed record ChatLineMessage(ulong MessageId,uint RequestId,ushort ChannelId,uint SenderId,string Sender,string Text,long Timestamp,string RussianText="",Vec3d? SignalPosition=null):IProtocolMessage {public MessageType Type=>MessageType.ChatLine;}
public sealed record ChatAckMessage(ulong MessageId):IProtocolMessage {public MessageType Type=>MessageType.ChatAck;}
public sealed record ChatRejectedMessage(uint RequestId,ushort ChannelId,ChatError Error):IProtocolMessage {public MessageType Type=>MessageType.ChatRejected;}
