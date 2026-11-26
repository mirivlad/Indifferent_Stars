using System;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class NetworkClient
{
    private (CombatCommandMessage Message, double Started, double LastSent)? _pendingCombat;
    public event Action<CombatStatusMessage>? CombatStatusReceived;
    public event Action<CombatReplyMessage>? CombatReplied;
    public event Action<CombatEventMessage>? CombatEventReceived;
    public event Action<WreckContactsMessage>? WreckContactsReceived;

    public void RequestCombat(CombatCommand command, uint targetId = 0, byte slot = 0)
    {
        if (State != ConnectionState.Connected) return;
        var message = new CombatCommandMessage(_requestSequence++, command, targetId, slot);
        var now = NowSeconds();
        _pendingCombat = (message, now, now);
        Send(message);
    }

    private void RetryCombat()
    {
        if (_pendingCombat is not { } pending) return;
        var now = NowSeconds();
        if (now - pending.Started > 3)
        {
            _pendingCombat = null;
            CombatReplied?.Invoke(new CombatReplyMessage(pending.Message.RequestId, false, pending.Message.Command, pending.Message.TargetId));
        }
        else if (State == ConnectionState.Connected && now - pending.LastSent > .4)
        {
            _pendingCombat = (pending.Message, pending.Started, now);
            Send(pending.Message);
        }
    }
}
