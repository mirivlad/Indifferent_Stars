using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using System.Collections.Generic;
namespace IndifferentStars.Simulation;
public sealed class NpcWork
{
    public required NpcOccupation Occupation { get; init; }
    public NpcPhase Phase { get; set; }
    public int HomeStationId { get; set; }
    public int TargetId { get; set; }
    public Vec3d Goal { get; set; }
    public double Until { get; set; }
    public ushort CommodityId { get; set; }
    public int Cargo { get; set; }
    public int CompletedJobs { get; set; }
    public int Deliveries { get; set; }
    public bool FaultUsed { get; set; }
    public NpcSecurity Security { get; set; } = new();
}

public sealed class NpcSecurity
{
    public uint ThreatId { get; set; }
    public double ThreatUntil { get; set; }
    public double NextDecision { get; set; }
    public double NextContactReport { get; set; }
    public double RouteAvoidanceUntil { get; set; }
    public bool Returning { get; set; }
    public bool InterruptedAssignment { get; set; }
    public uint EscortId { get; set; }
    public double EscortUntil { get; set; }
    public double EscortAvailableAt { get; set; }
    public ulong SalvageTarget { get; set; }
    public int Replacements { get; set; }
    public double ReplacementAt { get; set; }
    public Dictionary<ushort, int> Loot { get; set; } = new();
}
public sealed record WorldActivity(ulong Sequence,uint SourceId,string Source,Vec3d Position,int TargetId,string Kind,string English,string Russian,bool SystemWide,double TimeSeconds);
