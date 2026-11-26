using System;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class NetworkClient
{
    private (ShipyardRequestMessage Message, double Started, double Sent)? _pendingShipyard;
    private uint _lastShipyardReply;
    public event Action<ShipyardReplyMessage>? ShipyardReplied;

    public void RequestShipyard(ShipyardAction action, ushort catalogId = 0, uint vesselId = 0, byte slot = 0)
    {
        if (State != ConnectionState.Connected || _pendingShipyard.HasValue) return;
        var request = new ShipyardRequestMessage(_requestSequence++, action, catalogId, vesselId, slot, _clientId);
        var now = NowSeconds();
        _pendingShipyard = (request, now, now);
        Send(request);
    }

    private void ReceiveShipyard(ShipyardReplyMessage reply)
    {
        if (reply.RequestId < _lastShipyardReply || _pendingShipyard?.Message.RequestId != reply.RequestId) return;
        _lastShipyardReply = reply.RequestId;
        _pendingShipyard = null;
        ShipyardReplied?.Invoke(reply);
    }

    private void RetryShipyard()
    {
        if (_pendingShipyard is not { } pending) return;
        var now = NowSeconds();
        if (now - pending.Started > 4)
        {
            _pendingShipyard = null;
            ShipyardReplied?.Invoke(new(pending.Message.RequestId, pending.Message.Action, ShipyardError.Unavailable, null));
        }
        else if (State == ConnectionState.Connected && now - pending.Sent > .4)
        {
            _pendingShipyard = (pending.Message, pending.Started, now);
            Send(pending.Message);
        }
    }
}
