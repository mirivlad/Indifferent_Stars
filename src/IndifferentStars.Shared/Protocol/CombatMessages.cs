namespace IndifferentStars.Shared.Protocol;

public enum WeaponKind : byte
{
    None = 0,
    PulseLaser = 1,
    BeamLaser = 2,
    Cannon = 3,
    Missile = 4,
    Turret = 5,
}

public enum CombatCommand : byte { Lock = 1, Attack, Stop, Escort, KeepDistance, Retreat, SelectWeapon, Repair, Recover }
public enum CombatOrder : byte { None, Attack, Escort, KeepDistance, Retreat }
public enum CombatEventKind : byte { Fired = 1, ShieldHit, HullHit, Destroyed, MissileTrail }

public sealed record CombatCommandMessage(uint RequestId, CombatCommand Command, uint TargetId = 0, byte Slot = 0) : IProtocolMessage
{
    public MessageType Type => MessageType.CombatCommand;
}

public sealed record CombatReplyMessage(uint RequestId, bool Accepted, CombatCommand Command, uint TargetId) : IProtocolMessage
{
    public MessageType Type => MessageType.CombatReply;
}

public readonly record struct WeaponStatus(WeaponKind Kind, ushort Loaded, ushort Reserve, float ReadyIn);
public sealed record CombatStatusMessage(
    uint TargetId, CombatOrder Order, byte SelectedSlot,
    float Energy, float Shield, float Armor, float Hull,
    float Engines, float Sensors, float Weapons,
    bool Destroyed, float RecoveryIn, WeaponStatus[] Slots) : IProtocolMessage
{
    public const int MaxSlots = 4;
    public MessageType Type => MessageType.CombatStatus;
}

public sealed record CombatEventMessage(
    ulong Sequence, uint SourceId, uint TargetId, CombatEventKind Kind, WeaponKind Weapon,
    Vec3d From, Vec3d To) : IProtocolMessage
{
    public MessageType Type => MessageType.CombatEvent;
}

public readonly record struct WreckContact(ulong Id, Vec3d Position);

public sealed record WreckContactsMessage(ulong Tick, WreckContact[] Contacts) : IProtocolMessage
{
    public const int MaxContacts = 24;
    public MessageType Type => MessageType.WreckContacts;
}
