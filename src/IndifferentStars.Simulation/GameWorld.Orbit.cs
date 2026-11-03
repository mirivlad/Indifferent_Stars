using System;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed partial class GameWorld
{
    public OrbitResultMessage StartOrbit(uint playerId, OrbitRequestMessage request)
    {
        var body = Catalog.FindCelestial((int)request.TargetId);
        if (body is null) return new(request.RequestId, request.TargetId, OrbitError.UnknownTarget);
        var minimum = NavigationLimits.OrbitMinimum(body);
        if (!NavigationLimits.ValidDistance(request.Altitude) || request.Altitude < minimum)
            return new(request.RequestId, request.TargetId, OrbitError.UnsafeAltitude, minimum);
        var ship = GetPlayer(playerId);
        if (ship is null || ship.Docked || ship.InWarp)
            return new(request.RequestId, request.TargetId, OrbitError.Unavailable, minimum);
        var center = Catalog.CelestialOffset(body);
        var radial = ship.Position - center;
        radial = new Vec3d(radial.X, 0, radial.Z).Normalized();
        if (radial.LengthSquared < .5) radial = new Vec3d(1, 0, 0);
        var entry = center + radial * (body.Radius + request.Altitude);
        if (!SetRoute(playerId, new RouteRequestMessage(request.RequestId, new[] { new NavigationPoint(0, entry) }, true)))
            return new(request.RequestId, request.TargetId, OrbitError.Unavailable, minimum);
        ship.ApproachFollowing = false;
        ship.OrbitTargetId = request.TargetId;
        ship.OrbitAltitude = request.Altitude;
        ship.OrbitRetrograde = request.Retrograde;
        return new(request.RequestId, request.TargetId, OrbitError.None, minimum);
    }

    private bool StepOrbit(ShipState ship, double dt)
    {
        if (ship.OrbitTargetId == 0) return false;
        if (!ship.OrbitEstablished)
        {
            if (ship.RouteActive) return false;
            if (ship.RouteIndex < ship.NavigationRoute.Length ||
                !ApproachPilot.Arrived(ship, ship.NavigationRoute[^1].Position!.Value))
            { ship.CancelOrbit(); return false; }
            ship.OrbitEstablished = true;
            ship.ApproachTargetId = ship.OrbitTargetId;
        }
        if (!CanCruise(ship))
        {
            ship.CancelOrbit(); ship.ApproachTargetId = 0;
            ship.CruiseBraking = ship.Velocity.Length > WorldCatalog.MaxBoostSpeed;
            return false;
        }
        var body = Catalog.FindCelestial((int)ship.OrbitTargetId)!;
        OrbitPilot.Step(ship, Catalog.CelestialOffset(body), body.Radius + ship.OrbitAltitude, dt);
        return true;
    }
}
