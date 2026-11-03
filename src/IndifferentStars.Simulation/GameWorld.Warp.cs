using System;
using System.Linq;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed partial class GameWorld
{
    public static WarpResultMessage StaleWarp(uint requestId, uint targetId) => new(requestId, targetId, false,
        ServerText.Get("en", "warp_stale"), Vec3d.Zero, 0, 0, ServerText.Get("ru", "warp_stale"));

    public WarpOutcome Warp(uint playerId, uint targetId, Vec3d? destination = null, bool fromRoute = false, double? surfaceDistance = null)
        => WarpShip(GetPlayer(playerId),targetId,destination,fromRoute,surfaceDistance);

    private WarpOutcome WarpShip(ShipState? player,uint targetId,Vec3d? destination=null,bool fromRoute=false,double? surfaceDistance=null)
    {
        if (player is null) return WarpFail(targetId, "unknown_ship");
        if (player.Docked) return WarpFail(targetId, "warp_undock");
        if (player.InWarp) return WarpFail(targetId, "warp_busy");
        if (destination is { } point && (targetId != 0 || !point.IsFinite))
            return WarpFail(targetId, "warp_invalid");

        if (surfaceDistance is { } requested && !NavigationLimits.ValidDistance(requested))
            return WarpFail(targetId, "warp_distance_invalid");
        var minimum = NavigationLimits.LocalWarpMinimum;
        Vec3d arrival;
        var label = ServerText.Get("en", "warp_waypoint");
        var russianLabel = ServerText.Get("ru", "warp_waypoint");
        if (destination is { } waypoint)
            arrival = surfaceDistance is { } gap ? waypoint - (waypoint - player.Position).Normalized() * gap : waypoint;
        else
        {
            var targetShip = _npcs.FirstOrDefault(n => n.Id == targetId) ?? _players.GetValueOrDefault(targetId);
            var body = Catalog.Find((int)targetId);
            var celestial = Catalog.FindCelestial((int)targetId);
            if (targetShip?.Id == player.Id) return WarpFail(targetId, "warp_self");
            if (targetShip is not null && (targetShip.InWarp ||
                Vec3d.Distance(player.Position, targetShip.Position) > WorldCatalog.RelevanceRange))
                return WarpFail(targetId, "warp_unknown");
            if (targetShip is null && body is null && celestial is null) return WarpFail(targetId, "warp_unknown");
            minimum = celestial is not null ? NavigationLimits.CelestialWarpMinimum(celestial) : NavigationLimits.LocalWarpMinimum;
            if (surfaceDistance is { } requestedGap && requestedGap < minimum)
                return WarpFail(targetId, "warp_distance_min", minimum);
            var desiredGap = surfaceDistance ?? Math.Max(minimum, (celestial?.AtmosphereHeight ?? 0) + WarpFlight.Clearance + SurfaceMesh.Ship.Radius);
            var center = targetShip?.Position ?? body?.Position ?? Catalog.CelestialOffset(celestial!);
            var distance = Vec3d.Distance(player.Position, center);
            if (!double.IsFinite(distance) || distance < 1) return WarpFail(targetId, "warp_invalid");
            var direction = (center - player.Position) / distance;
            if (celestial is not null)
                arrival = center - direction * (celestial.Radius + desiredGap);
            else
            {
                var surface = targetShip is not null ? WorldSurfaces.Ship(targetShip, targetShip.Position, targetShip.Orientation)
                    : BodySurface(body!, TimeSeconds + WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds);
                var hit = SurfaceQuery.Raycast(surface.Mesh, surface.Local(player.Position), surface.Local(center));
                if (hit is null) return WarpFail(targetId, "warp_invalid");
                var low = Vec3d.Distance(Vec3d.Lerp(player.Position, center, hit.Value), center);
                var high = surface.Radius + desiredGap;
                for (var i = 0; i < 40; i++)
                {
                    var middle = (low + high) / 2;
                    if (surface.Distance(center - direction * middle) < desiredGap)
                        low = middle;
                    else high = middle;
                }
                arrival = center - direction * high;
            }
            label = russianLabel = targetShip?.Name ?? body?.Name ?? celestial!.Name;
        }

        if (!arrival.IsFinite || !double.IsFinite(Vec3d.Distance(player.Position, arrival)))
            return WarpFail(targetId, "warp_invalid");
        if (Vec3d.Distance(player.Position, arrival) < 1) return WarpFail(targetId, "warp_close");
        if (!WarpSafe(player, player.Position, TimeSeconds)) return WarpFail(targetId, "warp_departure_blocked");
        if (!WarpSafe(player, arrival, TimeSeconds + WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds, targetId, minimum))
            return WarpFail(targetId, "warp_blocked");
        var directionToArrival = (arrival - player.Position).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(directionToArrival);
        if (!fromRoute) { player.RouteActive = false; player.CancelOrbit(); }
        player.WarpTargetId = targetId; player.WarpTargetMinimum = minimum;
        player.WarpStart = player.Position;
        player.WarpDestination = arrival;
        player.WarpInitialOrientation = player.Orientation;
        player.WarpOrientation = Quaternion.CreateFromYawPitchRoll((float)yaw, (float)pitch, 0);
        player.WarpStartedAt = TimeSeconds;
        player.WarpStage = WarpStage.Charging;
        player.WarpProgress = 0;
        player.ApproachTargetId = 0;
        player.DockingGranted = false;
        player.Cruising = false;
        player.CruiseBraking = false;
        player.CruiseInhibited = true;
        player.Velocity = Vec3d.Zero;
        player.Throttle = 0;
        if(_inputs.TryGetValue(player.Id,out var input))input.Input=InputState.None;
        return new WarpOutcome(true, targetId, ServerText.Get("en", "warp_accepted", label),
            arrival, (float)yaw, (float)pitch, ServerText.Get("ru", "warp_accepted", russianLabel));
    }

    private bool WarpSafe(ShipState player, Vec3d position, double time, uint targetId = 0, double targetMinimum = 0)
    {
        var clearance = WarpFlight.Clearance + SurfaceMesh.Ship.Radius;
        var tolerance = Math.Max(1e-5, position.Length * 2e-15);
        foreach (var body in _collisionBodies)
        {
            var pose = BodySurface(body, time);
            var gap = body.Id == targetId ? targetMinimum : clearance;
            if (Vec3d.Distance(position, pose.Position) < pose.Radius + gap &&
                pose.Distance(position) < gap - tolerance) return false;
        }
        foreach (var other in _players.Values.Concat(_npcs))
        {
            if (other.Id == player.Id || other.InWarp) continue;
            var pose = WorldSurfaces.Ship(other, other.Position, other.Orientation);
            var gap = other.Id == targetId ? targetMinimum : clearance;
            if (Vec3d.Distance(position, other.Position) < pose.Radius + gap &&
                pose.Distance(position) < gap - tolerance) return false;
        }
        return Catalog.Celestials.All(body =>
            Vec3d.Distance(position, Catalog.CelestialOffset(body)) >= body.Radius + (body.Id == targetId ? targetMinimum : Math.Max(body.AtmosphereHeight + clearance, NavigationLimits.CelestialWarpMinimum(body))) - tolerance);
    }

    private void StepWarp(ShipState ship, double dt)
    {
        var elapsed = Math.Round(TimeSeconds + dt - ship.WarpStartedAt, 9);
        if (elapsed < WarpFlight.ChargeSeconds)
        {
            ship.WarpProgress = (float)(elapsed / WarpFlight.ChargeSeconds);
            ship.Orientation = Quaternion.Slerp(ship.WarpInitialOrientation, ship.WarpOrientation,
                ship.WarpProgress * ship.WarpProgress * (3 - 2 * ship.WarpProgress));
            return;
        }
        if (ship.WarpStage == WarpStage.Charging)
        {
            if (!WarpSafe(ship, ship.WarpDestination, ship.WarpStartedAt + WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds, ship.WarpTargetId, ship.WarpTargetMinimum))
            {
                ship.WarpStage = WarpStage.None;
                ship.WarpProgress = 0;
                return;
            }
            ship.WarpStage = WarpStage.Travel;
        }
        var travel = Math.Clamp((elapsed - WarpFlight.ChargeSeconds) / WarpFlight.TravelSeconds, 0, 1);
        if (ship.WarpStage == WarpStage.Travel)
        {
            ship.Orientation = ship.WarpOrientation;
            var eased = travel * travel * (3 - 2 * travel);
            ship.Position = Vec3d.Lerp(ship.WarpStart, ship.WarpDestination, eased);
            ship.Velocity = (ship.WarpDestination - ship.WarpStart) * (6 * travel * (1 - travel) / WarpFlight.TravelSeconds);
            ship.WarpProgress = (float)travel;
            if (travel < 1) return;
            ship.Position = ship.WarpDestination;
            if (!WarpSafe(ship, ship.WarpDestination, TimeSeconds + dt, ship.WarpTargetId, ship.WarpTargetMinimum))
            {
                // Materialise only at a checked location, even if another ship occupied the destination in transit.
                var fallback = ship.WarpStart;
                var attempt = 0;
                while (!WarpSafe(ship, fallback, TimeSeconds + dt))
                {
                    if (++attempt > 64) throw new InvalidOperationException("No safe warp recovery position found.");
                    fallback = ship.WarpStart + Vec3d.Up * (10000 * Math.Pow(2, attempt));
                }
                ship.Position = fallback;
                ship.WarpDestination = fallback;
                ship.WarpStage = WarpStage.Recovery;
            }
            else ship.WarpStage = WarpStage.Exit;
            ship.Velocity = Vec3d.Zero;
        }
        ship.WarpProgress = (float)Math.Clamp((elapsed - WarpFlight.ChargeSeconds - WarpFlight.TravelSeconds) / WarpFlight.ExitSeconds, 0, 1);
        if (ship.WarpProgress < 1) return;
        ship.WarpStage = WarpStage.None;
        ship.WarpProgress = 0;
        if(_inputs.TryGetValue(ship.Id,out var input))input.Input=InputState.None;
    }

    private static WarpOutcome WarpFail(uint targetId, string key, params object[] args) =>
        new(false, targetId, ServerText.Get("en", key, args), Vec3d.Zero, 0, 0, ServerText.Get("ru", key, args));
}
