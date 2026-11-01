using System;
using System.Numerics;

namespace IndifferentStars.Shared.Protocol;

public static class ProtocolConstants
{
    public const byte Version = 3;
    public const byte Magic0 = (byte)'I';
    public const byte Magic1 = (byte)'S';
    public const int HeaderSize = 4;
    public const int MaxPacketSize = 1400;
    public const int MaxNameBytes = 48;
    public const int MaxEntitiesPerSnapshot = 64;
    public const int MaxStringBytes = 256;
    public const int MaxTextBytes = 4000;

    public static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(10);
}

public enum MessageType : byte
{
    Hello = 1,
    HelloAccepted = 2,
    HelloRejected = 3,
    Input = 4,
    Snapshot = 5,
    ScanRequest = 6,
    ScanResult = 7,
    WarpRequest = 8,
    WarpResult = 9,
    Ping = 10,
    Pong = 11,
    Disconnect = 12,
    ApproachRequest = 13,
    ApproachResult = 14,
}

public enum EntityKind : byte
{
    Player = 1,
    Npc = 2,
}

public interface IProtocolMessage
{
    MessageType Type { get; }
}

public sealed record HelloMessage(Guid ClientId, string ClientName) : IProtocolMessage
{
    public MessageType Type => MessageType.Hello;
}

public sealed record HelloAcceptedMessage(
    uint PlayerId,
    string PlayerName,
    Vec3d Spawn,
    float SpawnYaw,
    int WorldSeed,
    string SystemName,
    float ScanRange,
    float WarpRange,
    float MaxSpeed,
    ulong ServerTick,
    byte TickRate) : IProtocolMessage
{
    public MessageType Type => MessageType.HelloAccepted;
}

public sealed record HelloRejectedMessage(string Reason) : IProtocolMessage
{
    public MessageType Type => MessageType.HelloRejected;
}

/// <summary>
/// Player intent for one control interval. Values are normalized to -127..127.
/// The client never sends positions: the server integrates all movement.
/// </summary>
public sealed record InputMessage(
    uint Sequence,
    sbyte Throttle,
    sbyte Steer,
    sbyte Pitch,
    sbyte Vertical,
    bool Boost,
    bool Brake,
    sbyte Strafe = 0,
    sbyte Roll = 0) : IProtocolMessage
{
    public MessageType Type => MessageType.Input;
}

public sealed record EntitySnapshot(
    uint Id,
    EntityKind Kind,
    string Name,
    Vec3d Position,
    float Yaw,
    float Pitch,
    Vec3d Velocity,
    sbyte Throttle,
    Quaternion Orientation = default,
    uint ApproachTargetId = 0)
{
}

public sealed record SnapshotMessage(ulong Tick, ulong ServerTimeMs, EntitySnapshot[] Entities) : IProtocolMessage
{
    public MessageType Type => MessageType.Snapshot;
}

public sealed record ScanRequestMessage(uint RequestId, uint TargetId) : IProtocolMessage
{
    public MessageType Type => MessageType.ScanRequest;
}

public sealed record ScanResultMessage(
    uint RequestId,
    uint TargetId,
    bool Success,
    string Title,
    string Body) : IProtocolMessage
{
    public MessageType Type => MessageType.ScanResult;
}

public sealed record WarpRequestMessage(uint RequestId, uint TargetId) : IProtocolMessage
{
    public MessageType Type => MessageType.WarpRequest;
}

public sealed record WarpResultMessage(
    uint RequestId,
    uint TargetId,
    bool Success,
    string Message,
    Vec3d NewPosition,
    float Yaw,
    float Pitch) : IProtocolMessage
{
    public MessageType Type => MessageType.WarpResult;
}

public sealed record PingMessage(ulong ClientTimeMs) : IProtocolMessage
{
    public MessageType Type => MessageType.Ping;
}

public sealed record PongMessage(ulong ClientTimeMs) : IProtocolMessage
{
    public MessageType Type => MessageType.Pong;
}

public sealed record DisconnectMessage(string Reason) : IProtocolMessage
{
    public MessageType Type => MessageType.Disconnect;
}

public sealed record ApproachRequestMessage(uint TargetId) : IProtocolMessage
{
    public MessageType Type => MessageType.ApproachRequest;
}

public sealed record ApproachResultMessage(uint TargetId, bool Accepted) : IProtocolMessage
{
    public MessageType Type => MessageType.ApproachResult;
}
