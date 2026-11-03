using System;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed partial class GameWorld
{
    public bool SetRoute(uint playerId, RouteRequestMessage request)
    {
        var ship = GetPlayer(playerId);
        if (ship is null || request.Points.Length > RouteRequestMessage.MaxPoints ||
            (request.Enabled && (ship.Docked || request.Points.Length == 0))) return false;
        foreach (var stop in request.Points)
        {
            if (stop.Position is { } point)
            {
                if (stop.TargetId != 0 || !point.IsFinite || !double.IsFinite(Vec3d.Distance(ship.Position, point))) return false;
            }
            else if (stop.TargetId == ship.Id || !TryRoutePosition(ship, stop, out _)) return false;
        }
        ship.NavigationRoute = request.Points.ToArray();
        ship.RouteRevision = request.RequestId;
        ship.RouteIndex = 0;
        ship.RouteActive = request.Enabled;
        if (!request.Enabled && (ship.Cruising || ship.Velocity.Length > WorldCatalog.MaxBoostSpeed)) ship.CruiseBraking = true;
        ship.RouteWarping = ship.InWarp && request.Enabled;
        ship.RouteAdvanceAfterWarp = false;
        ship.RouteCruiseSeconds = 0;
        ship.ApproachTargetId = 0;
        ship.DockingGranted = false;
        _inputs[playerId].Input = InputState.None;
        return true;
    }

    private bool TryRoutePosition(ShipState ship, NavigationPoint stop, out Vec3d position)
    {
        position = stop.Position ?? Vec3d.Zero;
        if (stop.Position.HasValue) return true;
        if (Catalog.FindCelestial((int)stop.TargetId) is { } celestial)
        {
            var center = Catalog.CelestialOffset(celestial);
            position = center + (ship.Position - center).Normalized() * (celestial.Radius + celestial.AtmosphereHeight + 2500);
            return true;
        }
        if (Catalog.Find((int)stop.TargetId) is { } body)
        {
            position = body.Position + (ship.Position - body.Position).Normalized() * (body.Radius + 1500);
            return true;
        }
        var other = _players.GetValueOrDefault(stop.TargetId) ?? _npcs.FirstOrDefault(n => n.Id == stop.TargetId);
        if (other is null || other.InWarp || Vec3d.Distance(ship.Position, other.Position) > WorldCatalog.RelevanceRange) return false;
        position = other.Position + (ship.Position - other.Position).Normalized() * 1500;
        return true;
    }

    private InputState StepRoute(ShipState ship, double dt)
    {
        if (ship.RouteWarping)
        {
            ship.RouteWarping = false;
            // Only advance after reaching the actual server-approved warp endpoint.
            if (Vec3d.Distance(ship.Position, ship.WarpDestination) > 20)
            { ship.RouteActive = false; return InputState.None with { Brake = true }; }
            if (ship.RouteAdvanceAfterWarp) ship.RouteIndex++;
            ship.RouteCruiseSeconds = 0;
        }
        if (ship.RouteIndex >= ship.NavigationRoute.Length)
        { ship.RouteActive = false; ship.ApproachTargetId = 0; return InputState.None with { Brake = true }; }
        var stop = ship.NavigationRoute[ship.RouteIndex];
        if (!TryRoutePosition(ship, stop, out var destination))
        { ship.RouteActive = false; ship.ApproachTargetId = 0; return InputState.None with { Brake = true }; }
        ship.ApproachTargetId = stop.TargetId == 0 ? ship.Id : stop.TargetId;
        ship.ApproachDestination = destination;
        var distance = Vec3d.Distance(ship.Position, destination);
        if (ApproachPilot.Arrived(ship, destination))
        { ship.RouteIndex++; ship.RouteCruiseSeconds = 0; return InputState.None; }
        if (distance < 1_000_000 && !ship.Cruising && ship.Velocity.Length <= WorldCatalog.MaxBoostSpeed)
            return ApproachPilot.Steer(ship, destination, maxSpeed: WorldCatalog.MaxBoostSpeed * .8) with { Boost = true };

        // Leave the nearest solid surface before cruise. CCD still checks every ordinary-flight step.
        var nearest = double.MaxValue;
        var escape = Vec3d.Zero;
        foreach (var body in Catalog.LocalBodies)
        {
            var surface = BodySurface(body, TimeSeconds);
            var gap = surface.Distance(ship.Position);
            if (gap >= nearest) continue;
            nearest = gap;
            const double h = 2;
            double D(double x, double y, double z) => surface.Distance(ship.Position + new Vec3d(x, y, z));
            escape = new Vec3d(D(h,0,0)-D(-h,0,0), D(0,h,0)-D(0,-h,0), D(0,0,h)-D(0,0,-h)).Normalized();
        }
        foreach (var body in new[] { Catalog.Star, Catalog.Planet })
        {
            var offset = ship.Position - Catalog.CelestialOffset(body);
            var gap = offset.Length - body.Radius - body.AtmosphereHeight;
            if (gap >= nearest) continue;
            nearest = gap; escape = offset.Normalized();
        }
        foreach (var other in _players.Values.Concat(_npcs).Where(s => s.Id != ship.Id))
        {
            var offset = ship.Position - other.Position;
            var gap = offset.Length - 30;
            if (gap >= nearest) continue;
            nearest = gap; escape = offset.Normalized();
        }
        if (nearest < WorldCatalog.CruiseClearance + 2000)
        {
            if (escape.LengthSquared < .5) { ship.RouteActive = false; return InputState.None with { Brake = true }; }
            return ApproachPilot.Steer(ship, ship.Position + escape * 20_000, maxSpeed: WorldCatalog.MaxBoostSpeed * .8) with { Boost = true };
        }
        var steer = ApproachPilot.Steer(ship, destination);
        var aligned = Vec3d.Dot(ship.Forward, (destination - ship.Position).Normalized()) > .995;
        if (ship.Cruising && aligned && ship.Velocity.Length >= WorldCatalog.CruiseSpeed * .9) ship.RouteCruiseSeconds += dt;
        if (ship.RouteCruiseSeconds >= 1)
        {
            var warp = Warp(ship.Id, stop.TargetId, stop.Position, fromRoute: true);
            if (warp.Success) { ship.RouteWarping = true; ship.RouteAdvanceAfterWarp = true; }
            else { ship.RouteActive = false; ship.ApproachTargetId = 0; ship.CruiseBraking = ship.Velocity.Length > WorldCatalog.MaxBoostSpeed; }
            return InputState.None with { Brake = !warp.Success };
        }
        return steer with { Throttle = aligned ? 1 : 0, Cruise = aligned && CanCruise(ship), FlightAssist = true };
    }
}
