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

    public WarpOutcome Warp(uint playerId, uint targetId, Vec3d? destination = null)
    {
        var player = GetPlayer(playerId);
        if (player is null) return WarpFail(targetId, "unknown_ship");
        if (player.Docked) return WarpFail(targetId, "warp_undock");
        if (player.InWarp) return WarpFail(targetId, "warp_busy");
        if (destination is { } point && (targetId != 0 || !point.IsFinite))
            return WarpFail(targetId, "warp_invalid");

        Vec3d arrival;
        var label = ServerText.Get("en", "warp_waypoint");
        var russianLabel = ServerText.Get("ru", "warp_waypoint");
        if (destination is { } waypoint) arrival = waypoint;
        else
        {
            var targetShip = _npcs.FirstOrDefault(n => n.Id == targetId) ?? _players.GetValueOrDefault(targetId);
            var body = Catalog.Find((int)targetId);
            var celestial = Catalog.FindCelestial((int)targetId);
            if (targetShip?.Id == playerId) return WarpFail(targetId, "warp_self");
            if (targetShip is not null && (targetShip.InWarp ||
                Vec3d.Distance(player.Position, targetShip.Position) > WorldCatalog.RelevanceRange))
                return WarpFail(targetId, "warp_unknown");
            if (targetShip is null && body is null && celestial is null) return WarpFail(targetId, "warp_unknown");
            var center = targetShip?.Position ?? body?.Position ?? Catalog.CelestialOffset(celestial!);
            var distance = Vec3d.Distance(player.Position, center);
            if (!double.IsFinite(distance) || distance < 1) return WarpFail(targetId, "warp_invalid");
            var direction = (center - player.Position) / distance;
            if (celestial is not null)
                arrival = center - direction * (celestial.Radius + celestial.AtmosphereHeight + WarpFlight.Clearance + SurfaceMesh.Ship.Radius);
            else
            {
                var surface = targetShip is not null ? WorldSurfaces.Ship(targetShip, targetShip.Position, targetShip.Orientation)
                    : BodySurface(body!, TimeSeconds + WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds);
                var hit = SurfaceQuery.Raycast(surface.Mesh, surface.Local(player.Position), surface.Local(center));
                if (hit is null) return WarpFail(targetId, "warp_invalid");
                var low = Vec3d.Distance(Vec3d.Lerp(player.Position, center, hit.Value), center);
                var high = surface.Radius + WarpFlight.Clearance + SurfaceMesh.Ship.Radius;
                for (var i = 0; i < 40; i++)
                {
                    var middle = (low + high) / 2;
                    if (surface.Distance(center - direction * middle) < WarpFlight.Clearance + SurfaceMesh.Ship.Radius)
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
        if (!WarpSafe(player, arrival, TimeSeconds + WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds))
            return WarpFail(targetId, "warp_blocked");
        var directionToArrival = (arrival - player.Position).Normalized();
        var (yaw, pitch) = FlightModel.AnglesForDirection(directionToArrival);
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
        _inputs[playerId].Input = InputState.None;
        return new WarpOutcome(true, targetId, ServerText.Get("en", "warp_accepted", label),
            arrival, (float)yaw, (float)pitch, ServerText.Get("ru", "warp_accepted", russianLabel));
    }

    private bool WarpSafe(ShipState player, Vec3d position, double time)
    {
        var clearance = WarpFlight.Clearance + SurfaceMesh.Ship.Radius;
        foreach (var body in _collisionBodies)
        {
            var pose = BodySurface(body, time);
            if (Vec3d.Distance(position, pose.Position) < pose.Radius + clearance &&
                pose.Distance(position) < clearance - 1e-5) return false;
        }
        foreach (var other in _players.Values.Concat(_npcs))
        {
            if (other.Id == player.Id || other.InWarp) continue;
            var pose = WorldSurfaces.Ship(other, other.Position, other.Orientation);
            if (Vec3d.Distance(position, other.Position) < pose.Radius + clearance &&
                pose.Distance(position) < clearance - 1e-5) return false;
        }
        return new[] { Catalog.Planet, Catalog.Star }.All(body =>
            Vec3d.Distance(position, Catalog.CelestialOffset(body)) >= body.Radius + body.AtmosphereHeight + clearance - 1e-5);
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
            if (!WarpSafe(ship, ship.WarpDestination, ship.WarpStartedAt + WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds))
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
            if (!WarpSafe(ship, ship.WarpDestination, TimeSeconds + dt))
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
        _inputs[ship.Id].Input = InputState.None;
    }

    private static WarpOutcome WarpFail(uint targetId, string key, params object[] args) =>
        new(false, targetId, ServerText.Get("en", key, args), Vec3d.Zero, 0, 0, ServerText.Get("ru", key, args));
}
