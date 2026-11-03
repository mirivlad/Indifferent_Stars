using System;
using System.Numerics;

namespace IndifferentStars.Shared.Protocol;

public static class ProtocolConstants
{
    public const byte Version = 19;
    public const byte Magic0 = (byte)'I';
    public const byte Magic1 = (byte)'S';
    public const int HeaderSize = 4;
    public const int MaxPacketSize = 1400;
    public const int MaxNameBytes = 48;
    public const int MaxEntitiesPerSnapshot = 64;
    public const int MaxStringBytes = 256;
    public const int MaxTextBytes = 4000;
    public const int MaxMarketEntries = 16;

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
    DockRequest = 15,
    DockResult = 16,
    MarketRequest = 17,
    MarketState = 18,
    TradeRequest = 19,
    TradeResult = 20,
    CommsRequest = 21,
    CommsResult = 22,
    ChatSend = 23,
    ChatLine = 24,
    ChatAck = 25,
    ChatRejected = 26,
    RouteRequest = 27,
    RouteResult = 28,
    OrbitRequest = 29,
    OrbitResult = 30,
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

public sealed record HelloMessage(Guid ClientId, string ClientName, string GeometryHash) : IProtocolMessage
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
    float WarpClearance,
    float MaxSpeed,
    ulong ServerTick,
    byte TickRate,
    int SystemIndex = 0) : IProtocolMessage
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
    sbyte Roll = 0,
    bool FlightAssist = false,
    bool Cruise = false) : IProtocolMessage
{
    public MessageType Type => MessageType.Input;
}

public sealed record ImpactState(uint Sequence, Vec3d Point, Vec3d Normal, float NormalSpeed, float TangentialSpeed);

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
    uint ApproachTargetId = 0,
    bool Docked = false,
    bool DockingGranted = false,
    bool FlightAssist = false,
    bool ApproachFollowing = false,
    bool Cruising = false,
    Vec3d? ApproachPoint = null,
    bool CruiseInhibited = false,
    bool CruiseBraking = false,
    WarpStage WarpStage = WarpStage.None,
    float WarpProgress = 0,
    Vec3d? WarpDestination = null,
    ImpactState? Impact = null,
    uint RouteRevision = 0,
    byte RouteIndex = 0,
    bool RouteActive = false,
    bool Boosting = false,
    uint OrbitTargetId = 0,
    double OrbitAltitude = 0,
    bool OrbitRetrograde = false,
    bool OrbitEstablished = false,
    uint DockingStationId = 0)
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
    string Body,
    string RussianTitle = "",
    string RussianBody = "") : IProtocolMessage
{
    public MessageType Type => MessageType.ScanResult;
}

public sealed record WarpRequestMessage(uint RequestId, uint TargetId, Vec3d? Destination = null, double? SurfaceDistance = null) : IProtocolMessage
{
    public MessageType Type => MessageType.WarpRequest;
}

public sealed record WarpResultMessage(
    uint RequestId,
    uint TargetId,
    bool Success,
    string Message,
    Vec3d Destination,
    float Yaw,
    float Pitch,
    string RussianMessage = "") : IProtocolMessage
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

public sealed record ApproachRequestMessage(uint TargetId, bool Follow = false, Vec3d? Waypoint = null) : IProtocolMessage
{
    public MessageType Type => MessageType.ApproachRequest;
}

public sealed record ApproachResultMessage(uint TargetId, bool Accepted) : IProtocolMessage
{
    public MessageType Type => MessageType.ApproachResult;
}

public sealed record DockRequestMessage(uint TargetId, bool Undock) : IProtocolMessage
{
    public MessageType Type => MessageType.DockRequest;
}

public sealed record DockResultMessage(bool Undock, bool Accepted) : IProtocolMessage
{
    public MessageType Type => MessageType.DockResult;
}

/// <summary>One row of the station market as seen by a specific player.</summary>
public sealed record MarketEntry(ushort CommodityId, int StationStock, int PlayerQuantity)
{
}

/// <summary>Player account and the station stock snapshot. Server-authoritative.</summary>
public sealed record MarketState(int Credits, int CargoUsed, int CargoCapacity, MarketEntry[] Entries)
{
}

public sealed record MarketRequestMessage(uint RequestId) : IProtocolMessage
{
    public MessageType Type => MessageType.MarketRequest;
}

public sealed record MarketStateMessage(uint RequestId, MarketState State) : IProtocolMessage
{
    public MessageType Type => MessageType.MarketState;
}

public sealed record TradeRequestMessage(uint RequestId, ushort CommodityId, int Quantity, bool Buy) : IProtocolMessage
{
    public MessageType Type => MessageType.TradeRequest;
}

public sealed record TradeResultMessage(
    uint RequestId,
    bool Success,
    string Message,
    string RussianMessage,
    MarketState State) : IProtocolMessage
{
    public MessageType Type => MessageType.TradeResult;
}


public sealed record CommsRequestMessage(uint TargetId) : IProtocolMessage
{
    public MessageType Type => MessageType.CommsRequest;
}

public sealed record CommsResultMessage(uint TargetId, bool Success, string Message, string RussianMessage) : IProtocolMessage
{
    public MessageType Type => MessageType.CommsResult;
}


public enum WarpStage : byte
{
    None,
    Charging,
    Travel,
    Exit,
    Recovery,
}

public static class WarpFlight
{
    public const double Clearance = 1000;
    public const double ChargeSeconds = 2;
    public const double TravelSeconds = 4;
    public const double ExitSeconds = 1;
}

public sealed record NavigationPoint(uint TargetId, Vec3d? Position = null);
public sealed record RouteRequestMessage(uint RequestId, NavigationPoint[] Points, bool Enabled) : IProtocolMessage
{
    public MessageType Type => MessageType.RouteRequest;
    public const int MaxPoints = 24;
}
public sealed record RouteResultMessage(uint RequestId, bool Accepted) : IProtocolMessage
{
    public MessageType Type => MessageType.RouteResult;
}

public enum OrbitError : byte { None, UnknownTarget, UnsafeAltitude, Unavailable, Stale, Timeout }
public sealed record OrbitRequestMessage(uint RequestId, uint TargetId, double Altitude, bool Retrograde = false) : IProtocolMessage
{
    public MessageType Type => MessageType.OrbitRequest;
}
public sealed record OrbitResultMessage(uint RequestId, uint TargetId, OrbitError Error, double MinimumAltitude = 0) : IProtocolMessage
{
    public MessageType Type => MessageType.OrbitResult;
    public bool Accepted => Error == OrbitError.None;
}
