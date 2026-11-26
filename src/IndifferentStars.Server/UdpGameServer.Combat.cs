using System.Net;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Server;

public sealed partial class UdpGameServer
{
    private double _lastWreckContactsSeconds;

    private void HandleCombat(IPEndPoint endpoint, double now, CombatCommandMessage request)
    {
        if (!_sessionsByEndpoint.TryGetValue(endpoint, out var session)) return;
        session.LastSeenSeconds = now;
        if (session.LastCombatResult is { } previous && request.RequestId <= previous.RequestId)
        {
            Send(endpoint, request.RequestId == previous.RequestId ? previous :
                new CombatReplyMessage(request.RequestId, false, request.Command, request.TargetId));
            return;
        }

        var accepted = _world.ApplyCombatCommand(session.PlayerId, request);
        var reply = new CombatReplyMessage(request.RequestId, accepted, request.Command, request.TargetId);
        session.LastCombatResult = reply;
        if (accepted && request.Command is CombatCommand.Repair or CombatCommand.Recover)
        {
            SaveWorld();
            if (request.Command == CombatCommand.Recover) SendWelcome(session);
            if (_world.GetMarket(session.PlayerId) is { } market)
                Send(endpoint, new MarketStateMessage(0, market));
        }
        Send(endpoint, reply);
    }

    private void BroadcastCombat(double now)
    {
        if (now - _lastCombatSeconds >= .10)
        {
            _lastCombatSeconds = now;
            foreach (var session in _sessions.Values)
                if (_world.BuildCombatStatus(session.PlayerId) is { } status) Send(session.Endpoint, status);
        }
        if (now - _lastWreckContactsSeconds >= .5)
        {
            _lastWreckContactsSeconds = now;
            foreach (var session in _sessions.Values)
                Send(session.Endpoint, _world.BuildWreckContacts(session.Ship));
        }
        while (_world.TryTakeCombatEvent(out var effect))
        {
            foreach (var session in _sessions.Values)
            {
                var position = session.Ship.Position;
                if (session.PlayerId == effect.SourceId || session.PlayerId == effect.TargetId ||
                    Vec3d.Distance(position, effect.From) <= WorldCatalog.RelevanceRange ||
                    Vec3d.Distance(position, effect.To) <= WorldCatalog.RelevanceRange)
                    Send(session.Endpoint, effect);
            }
        }
    }
}
