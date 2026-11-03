using System;
using System.Net;
using System.Collections.Generic;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

namespace IndifferentStars.Server;

public sealed class PlayerSession
{
    public uint TransponderSequence { get; set; }

    public required Guid ClientId { get; init; }
    public required uint PlayerId { get; init; }
    public required string Name { get; init; }
    public required IPEndPoint Endpoint { get; set; }
    public required ShipState Ship { get; set; }
    public double LastSeenSeconds { get; set; }
    public RouteResultMessage? LastRouteResult { get; set; }
    public uint LastInputSequence { get; set; }
    public uint? LastChatRequest { get; set; }
    public double LastChatAt { get; set; } = -100;
    public Dictionary<uint,IProtocolMessage> ChatReplies { get; } = new();
    public Dictionary<ulong,(ChatLineMessage Line,double Started,double Sent)> PendingChat { get; } = new();
    public double LastCommsAtSeconds { get; set; } = -100;
    public double LastScanAtSeconds { get; set; } = -100;
    public double LastWarpAtSeconds { get; set; } = -100;
    public OrbitResultMessage? LastOrbitResult { get; set; }
    public double LastOrbitAtSeconds { get; set; } = -100;
    public WarpResultMessage? LastWarpResult { get; set; }
    public TradeResultMessage? LastTradeResult { get; set; }
}
