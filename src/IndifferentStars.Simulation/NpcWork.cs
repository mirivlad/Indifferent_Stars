using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
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
}
public sealed record WorldActivity(ulong Sequence,uint SourceId,string Source,Vec3d Position,int TargetId,string Kind,string English,string Russian,bool SystemWide);
