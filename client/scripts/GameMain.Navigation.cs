using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class GameMain
{
    private NavigationPanel _navigation = null!;
    private readonly List<NavigationPoint> _routePoints = new();
    private List<NavigationPoint> _confirmedRoute = new();
    private uint _routeRevision;
    private uint _confirmedRevision;
    private bool _routeEnabled;
    private int _routeIndex;
    private double _navigationRefresh;

    private void SetupNavigation()
    {
        _navigation = new NavigationPanel(); AddChild(_navigation);
        _navigation.ContactSelected += (id, context) =>
        {
            _targetId=id; _hasTarget=true;
            if(context) { BuildContactMenu(); _contactMenu.Position=(Vector2I)GetViewport().GetMousePosition(); _contactMenu.Popup(); }
        };
        _navigation.RouteEdited += (index,direction) =>
        {
            if(_net?.IsRoutePending==true)return;
            var local=index-_routeIndex;
            TrimCompletedRoute();
            if(local<0||local>=_routePoints.Count)return;
            if(direction==0)_routePoints.RemoveAt(local);
            else if(local+direction>=0&&local+direction<_routePoints.Count)
                (_routePoints[local],_routePoints[local+direction])=(_routePoints[local+direction],_routePoints[local]);
            SubmitRoute(_routeEnabled&&_routePoints.Count>0);
        };
    }
    private void ConnectNavigation()
    {
        if(_net is null)return;
        _net.RouteCompleted += result =>
        {
            if(result.RequestId!=_routeRevision)return;
            if(result.Accepted){_confirmedRoute=_routePoints.ToList();_confirmedRevision=result.RequestId;}
            else
            {
                _routePoints.Clear();_routePoints.AddRange(_confirmedRoute);
                _routeRevision=_confirmedRevision;
                _routeIndex=_playerShip?.Track.RouteRevision==_confirmedRevision?_playerShip.Track.RouteIndex:0;
                _routeEnabled=_playerShip?.Track.RouteRevision==_confirmedRevision&&_playerShip.Track.RouteActive;
                _hud.ShowToast(Locale.Text("route_rejected"));
            }
        };
    }
    private void TrimCompletedRoute()
    {
        if(_routeIndex>0)_routePoints.RemoveRange(0,Math.Min(_routeIndex,_routePoints.Count));
        _routeIndex=0;
    }
    private void AddRoutePoint(NavigationPoint point)
    {
        if(_net?.State!=ConnectionState.Connected||_net.IsRoutePending)return;
        TrimCompletedRoute();
        if(_routePoints.Count>=RouteRequestMessage.MaxPoints) { _hud.ShowToast(Locale.Text("route_full")); return; }
        _routePoints.Add(point);
        // Editing a running route preserves its state; planning never starts a stopped pilot.
        SubmitRoute(_routeEnabled);
    }
    private void SubmitRoute(bool enabled)
    {
        if(_net?.State!=ConnectionState.Connected)return;
        _cruise=false; _cruiseStopping=false;
        _routeEnabled=enabled;_routeIndex=0;
        _routeRevision=_net.RequestRoute(_routePoints.ToArray(),enabled);
    }
    private void ToggleAutopilot()
    {
        if(_net?.State!=ConnectionState.Connected||_net.IsRoutePending)return;
        if(_routeEnabled || _playerShip?.Track is { ApproachTargetId: not 0 }) { TrimCompletedRoute(); SubmitRoute(false); return; }
        TrimCompletedRoute();
        if(_routePoints.Count==0)
        {
            if(!_hasTarget||FindContact(_targetId) is null){_hud.ShowToast(Locale.Text("choose_contact"));return;}
            _routePoints.Add(new NavigationPoint(_targetId));
        }
        SubmitRoute(true);
    }
    private Vec3d? RoutePosition(NavigationPoint point)
    {
        if(point.Position is { } position)return position;
        if(_catalog.Find((int)point.TargetId) is { } body)return body.Position;
        if(_catalog.FindCelestial((int)point.TargetId) is { } celestial)return celestial.SystemPosition-_catalog.LocalFrameOrigin;
        return _remoteShips.TryGetValue(point.TargetId,out var ship)&&ship.Track.HasData?ship.Track.WorldPosition:null;
    }
    private void UpdateNavigation()
    {
        if(_playerShip?.Track is { } track&&track.RouteRevision==_routeRevision&&_net?.IsRoutePending!=true)
        {
            _routeIndex=track.RouteIndex;
            if(_routeEnabled&&!track.RouteActive)_hud.ShowToast(Locale.Text(track.RouteIndex>=_routePoints.Count?"route_complete":"route_interrupted"));
            _routeEnabled=track.RouteActive;
        }
        _navigationRefresh-=GetProcessDeltaTime();
        if(_navigationRefresh>0)return;
        _navigationRefresh=.25;
        var observer=_playerShip?.Track.WorldPosition??Vec3d.Zero;
        var contacts=BuildContacts().Select(c=>new NavigationContact(c.Id,c.Name,c.Type,
            Math.Max(0,Vec3d.Distance(observer,RoutePosition(new NavigationPoint(c.Id))??observer)-c.Radius))).ToList();
        _navigation.UpdateContacts(contacts,_hasTarget?_targetId:0);
        _navigation.UpdateRoute(_routePoints.Select(p=>p.TargetId==0?Locale.Text("route_point"):FindContact(p.TargetId)?.Name??$"#{p.TargetId}").ToList(),_routeEnabled,_routeIndex,_net?.IsRoutePending==true);
        _tactical.RoutePoints.Clear();
        foreach(var point in _routePoints.Skip(_routeIndex))if(RoutePosition(point) is { } position)_tactical.RoutePoints.Add(position);
    }
    private void ShowObjectInformation()
    {
        if(FindContact(_targetId) is not { } contact)return;
        _navigation.ShowInformation(contact.Name,
            Locale.Text("type_"+contact.Type.ToLowerInvariant())+"\n"+
            Locale.Text("distance_label")+": "+NavigationPanel.Distance(contact.Distance)+"\n"+
            Locale.Text("altitude")+": "+(contact.RelativeAltitude<0?"−":"+")+NavigationPanel.Distance(Math.Abs(contact.RelativeAltitude))+"\n"+
            Locale.Text("information_sensors"));
    }
}
