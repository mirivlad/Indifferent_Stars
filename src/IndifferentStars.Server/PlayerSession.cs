using System;
using System.Net;
using IndifferentStars.Simulation;

namespace IndifferentStars.Server;

public sealed class PlayerSession
{
    public required Guid ClientId { get; init; }
    public required uint PlayerId { get; init; }
    public required string Name { get; init; }
    public required IPEndPoint Endpoint { get; set; }
    public required ShipState Ship { get; set; }
    public double LastSeenSeconds { get; set; }
    public uint LastInputSequence { get; set; }
    public double LastScanAtSeconds { get; set; } = -100;
    public double LastWarpAtSeconds { get; set; } = -100;
}