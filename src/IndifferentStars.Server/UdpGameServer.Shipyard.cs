using System.Net;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Server;

public sealed partial class UdpGameServer
{
    private void HandleShipyard(IPEndPoint endpoint, double now, ShipyardRequestMessage request)
    {
        if (!_sessionsByEndpoint.TryGetValue(endpoint, out var session) ||
            (session.ProfileKey is not null && request.ClientId != session.ClientId)) return;
        session.LastSeenSeconds = now;
        if (request.Action == ShipyardAction.Inspect)
        {
            Send(endpoint, _world.Shipyard(session.PlayerId, request));
            return;
        }
        if (session.LastShipyardResult is { } last && request.RequestId <= last.RequestId)
        {
            Send(endpoint, request.RequestId == last.RequestId ? last :
                new ShipyardReplyMessage(request.RequestId, request.Action, ShipyardError.Stale, null));
            return;
        }

        var reply = _world.Shipyard(session.PlayerId, request);
        session.LastShipyardResult = reply;
        if (session.ProfileKey is not null) SaveWorld();
        Send(endpoint, reply);
        if (reply.Error == ShipyardError.None && _world.GetMarket(session.PlayerId) is { } market)
            Send(endpoint, new MarketStateMessage(0, market));
    }
}
