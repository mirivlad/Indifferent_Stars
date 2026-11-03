using System;
namespace IndifferentStars.Shared.Protocol;

public enum ContactSource : byte { Tactical, Transponder, DistantSignal, LastKnown, Lost }
public sealed record SensorContact(uint Id, string Name, Vec3d Position, Vec3d Velocity,
    ContactSource Source, ulong ObservedAtMs, bool Transponder, uint DestinationId, EntityKind Kind = EntityKind.Npc);
public sealed record SensorContactsMessage(ulong Tick, SensorContact[] Contacts) : IProtocolMessage
{
    public MessageType Type => MessageType.SensorContacts;
    public const int MaxContacts = 8;
}
public sealed record TransponderMessage(uint Sequence, bool Enabled) : IProtocolMessage
{
    public MessageType Type => MessageType.Transponder;
}
