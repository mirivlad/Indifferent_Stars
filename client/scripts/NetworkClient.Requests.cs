using System;
using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class NetworkClient
{
    private (uint Id, uint Target, double StartedAt, double LastResendAt)? _pendingScan;
    private (uint Id, uint Target, IndifferentStars.Shared.Vec3d? Destination, double StartedAt, double LastResendAt)? _pendingWarp;
    private (uint Id, ushort Commodity, int Quantity, bool Buy, double StartedAt, double LastResendAt)? _pendingTrade;
    private double _lastPingSeconds;
    private (RouteRequestMessage Message, double Started, double Sent)? _pendingRoute;
    public event Action<RouteResultMessage>? RouteCompleted;
    public bool IsRoutePending => _pendingRoute.HasValue;
    public uint RequestRoute(NavigationPoint[] points, bool enabled)
    {
        var request = new RouteRequestMessage(_requestSequence++,points,enabled);
        var now = NowSeconds(); _pendingRoute = (request,now,now); Send(request); return request.RequestId;
    }


    public bool IsScanPending => _pendingScan.HasValue;
    public bool IsWarpPending => _pendingWarp.HasValue;
    public bool IsTradePending => _pendingTrade.HasValue;

    public void RequestComms(uint targetId)
    {
        if (State == ConnectionState.Connected) Send(new CommsRequestMessage(targetId));
    }

    public void RequestApproach(uint targetId, bool follow = false, IndifferentStars.Shared.Vec3d? waypoint = null)
    {
        if (State == ConnectionState.Connected)
            Send(new ApproachRequestMessage(targetId, follow, waypoint));
    }

    public void RequestDock(uint targetId, bool undock)
    {
        if (State == ConnectionState.Connected)
            Send(new DockRequestMessage(targetId, undock));
    }

    public void RequestScan(uint targetId)
    {
        if (State != ConnectionState.Connected || _pendingScan.HasValue)
            return;

        var now = NowSeconds();
        var id = _requestSequence++;
        _pendingScan = (id, targetId, now, now);
        Send(new ScanRequestMessage(id, targetId));
        Send(new PingMessage(Time.GetTicksMsec()));
    }

    public void RequestMarket()
    {
        if (State == ConnectionState.Connected)
            Send(new MarketRequestMessage(_requestSequence++));
    }

    public void RequestTrade(ushort commodityId, int quantity, bool buy)
    {
        if (State != ConnectionState.Connected || _pendingTrade.HasValue)
            return;

        var now = NowSeconds();
        var id = _requestSequence++;
        _pendingTrade = (id, commodityId, quantity, buy, now, now);
        Send(new TradeRequestMessage(id, commodityId, quantity, buy));
    }

    public void RequestWarp(uint targetId, IndifferentStars.Shared.Vec3d? destination = null)
    {
        if (State != ConnectionState.Connected || _pendingWarp.HasValue)
            return;

        var now = NowSeconds();
        var id = _requestSequence++;
        _pendingWarp = (id, targetId, destination, now, now);
        Send(new WarpRequestMessage(id, targetId, destination));
    }

    private void UpdatePendingRequests()
    {
        if (State != ConnectionState.Connected)
            return;

        var now = NowSeconds();
        if (_pendingRoute is { } route)
        {
            if (now-route.Started>3) { _pendingRoute=null; RouteCompleted?.Invoke(new RouteResultMessage(route.Message.RequestId,false)); }
            else if(now-route.Sent>.3) { _pendingRoute=(route.Message,route.Started,now); Send(route.Message); }
        }
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
                ScanTimedOut?.Invoke();
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
                _pendingWarp = (warp.Id, warp.Target, warp.Destination, warp.StartedAt, now);
                Send(new WarpRequestMessage(warp.Id, warp.Target, warp.Destination));
            }
        }

        if (_pendingTrade is { } trade)
        {
            if (now - trade.StartedAt > 3.0)
            {
                _pendingTrade = null;
            }
            else if (now - trade.LastResendAt > 0.5)
            {
                _pendingTrade = (trade.Id, trade.Commodity, trade.Quantity, trade.Buy, trade.StartedAt, now);
                Send(new TradeRequestMessage(trade.Id, trade.Commodity, trade.Quantity, trade.Buy));
            }
        }
    }

    private static double NowSeconds() => Time.GetTicksMsec() / 1000.0;
}
