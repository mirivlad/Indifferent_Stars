using System;
using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class NetworkClient
{
    private (uint Id, uint Target, double StartedAt, double LastResendAt)? _pendingScan;
    private (uint Id, uint Target, double StartedAt, double LastResendAt)? _pendingWarp;
    private double _lastPingSeconds;

    public bool IsScanPending => _pendingScan.HasValue;
    public bool IsWarpPending => _pendingWarp.HasValue;

    public void RequestApproach(uint targetId)
    {
        if (State == ConnectionState.Connected)
            Send(new ApproachRequestMessage(targetId));
    }

    public void RequestScan(uint targetId)
    {
        if (State != ConnectionState.Connected)
            return;

        var now = NowSeconds();
        var id = _requestSequence++;
        _pendingScan = (id, targetId, now, now);
        Send(new ScanRequestMessage(id, targetId));
        Send(new PingMessage(Time.GetTicksMsec()));
    }

    public void RequestWarp(uint targetId)
    {
        if (State != ConnectionState.Connected || _pendingWarp.HasValue)
            return;

        var now = NowSeconds();
        var id = _requestSequence++;
        _pendingWarp = (id, targetId, now, now);
        Send(new WarpRequestMessage(id, targetId));
    }

    private void UpdatePendingRequests()
    {
        if (State != ConnectionState.Connected)
            return;

        var now = NowSeconds();
        _lastPingSeconds += 0.05;
        if (_lastPingSeconds >= 1.0)
        {
            _lastPingSeconds = 0;
            Send(new PingMessage(Time.GetTicksMsec()));
        }

        if (_pendingScan is { } scan)
        {
            if (now - scan.StartedAt > 2.0)
            {
                _pendingScan = null;
            }
            else if (now - scan.LastResendAt > 0.3)
            {
                _pendingScan = (scan.Id, scan.Target, scan.StartedAt, now);
                Send(new ScanRequestMessage(scan.Id, scan.Target));
            }
        }

        if (_pendingWarp is { } warp)
        {
            if (now - warp.StartedAt > 2.0)
            {
                _pendingWarp = null;
            }
            else if (now - warp.LastResendAt > 0.4)
            {
                _pendingWarp = (warp.Id, warp.Target, warp.StartedAt, now);
                Send(new WarpRequestMessage(warp.Id, warp.Target));
            }
        }
    }

    private static double NowSeconds() => Time.GetTicksMsec() / 1000.0;
}
