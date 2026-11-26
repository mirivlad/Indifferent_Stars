using IndifferentStars.Shared.World;

namespace IndifferentStars.Shared.Protocol;

public enum ShipyardAction : byte
{
    Inspect, BuyHull, SellHull, ActivateHull, BuyModule, SellModule, FitModule, RemoveModule, Repair, Rearm,
}

public enum ShipyardError : byte
{
    None, DockRequired, Unavailable, Credits, Parts, Capacity, WrongStation, Incompatible, Limit, Stale,
}

public sealed record ShipyardRequestMessage(uint RequestId, ShipyardAction Action, ushort CatalogId = 0,
    uint VesselId = 0, byte Slot = 0, Guid ClientId = default) : IProtocolMessage
{
    public MessageType Type => MessageType.ShipyardRequest;
}

public readonly record struct YardPrice(ushort CatalogId, int Cost, bool InStock);
public readonly record struct HangarVessel(uint Serial, ushort HullId, uint StationId, float Integrity, FittedModule[] Modules,
    int Resale = 0);
public readonly record struct HangarModule(uint StationId, ushort ModuleId, ushort Quantity);

public sealed record ShipyardState(uint StationId, int Credits, uint ActiveHull, HangarVessel[] Vessels,
    HangarModule[] Modules, YardPrice[] HullOffers, YardPrice[] ModuleOffers, int RepairPrice, int RearmPrice)
{
    public const int MaxVessels = 12;
    public const int MaxModuleRows = 24;
    public const int MaxFitted = 16;
    public const int MaxOffers = 17;
}

public sealed record ShipyardReplyMessage(uint RequestId, ShipyardAction Action, ShipyardError Error, ShipyardState? State)
    : IProtocolMessage
{
    public MessageType Type => MessageType.ShipyardReply;
}
