using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class OrbitalNavigationTests
{
    [Theory]
    [InlineData("500 км", 500000)]
    [InlineData("500", 500000)]
    [InlineData("500 m", 500)]
    [InlineData("0,5 а.е.", 74798935350)]
    [InlineData("1 au", 149597870700)]
    public void DistanceInputAcceptsUnits(string input, double expected)
    {
        Assert.True(NavigationLimits.TryParseDistance(input, out var value));
        Assert.Equal(expected, value, 3);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity km")]
    [InlineData("-500 м")]
    [InlineData("0")]
    [InlineData("101 au")]
    [InlineData("500 parsec")]
    public void DistanceInputRejectsInvalidValues(string input) => Assert.False(NavigationLimits.TryParseDistance(input, out _));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrbitSustainsAltitudeAndMovesInChosenDirection(bool retrograde)
    {
        const double radius = 7_950_000;
        var ship = new ShipState { Id = 42, Name = "Orbiter", IsPlayer = true, Position = new Vec3d(radius, 0, 0), OrbitRetrograde = retrograde };
        for (var i = 0; i < 12_000; i++) OrbitPilot.Step(ship, Vec3d.Zero, radius, .05);
        Assert.InRange(Math.Abs(ship.Position.Length - radius), 0, 30);
        Assert.InRange(Math.Abs(ship.Position.Y), 0, .001);
        Assert.InRange(ship.Velocity.Length, 9900, 10010);
        Assert.True(retrograde ? ship.Position.Z > 1_000_000 : ship.Position.Z < -1_000_000);
        Assert.True(Math.Abs(Vec3d.Dot(ship.Position.Normalized(), ship.Velocity)) < 1);
    }

    [Fact]
    public void OrbitTransferDoesNotTeleportAndInvalidCommandsPreserveAcceptedOrbit()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101)); var ship = world.AddPlayer("Pilot");
        var body = world.Catalog.Planet; var start = ship.Position;
        ship.ApproachFollowing = true;
        var result = world.StartOrbit(ship.Id, new(1, (uint)body.Id, 500000, true));
        Assert.True(result.Accepted); Assert.Equal(start, ship.Position); Assert.True(ship.RouteActive);
        Assert.False(ship.ApproachFollowing);
        Assert.Equal(500000, ship.OrbitAltitude); Assert.True(ship.OrbitRetrograde);
        foreach (var altitude in new[] { 500d, double.NaN, double.PositiveInfinity, -1, NavigationLimits.MaximumDistance + 1 })
            Assert.False(world.StartOrbit(ship.Id, new(2, (uint)body.Id, altitude)).Accepted);
        Assert.False(world.StartOrbit(ship.Id, new(2, (uint)world.Catalog.Station.Id, 500000)).Accepted);
        Assert.Equal(1u, ship.RouteRevision); Assert.Equal((uint)body.Id, ship.OrbitTargetId);
        world.ApplyInput(ship.Id, InputState.None with { Strafe = 1 }); Assert.Equal(0u, ship.OrbitTargetId); Assert.False(ship.RouteActive);
    }

    [Fact]
    public void ArrivalEntersContinuousOrbitAndStoppingCancelsIt()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101)); var ship = world.AddPlayer("Pilot");
        var body = world.Catalog.Planet;
        ship.Position = world.Catalog.CelestialOffset(body) + new Vec3d(body.Radius + 500000, 0, 0);
        Assert.True(world.StartOrbit(ship.Id, new(1, (uint)body.Id, 500000)).Accepted);
        for (var i = 0; i < 100; i++) world.Tick(.05);
        Assert.True(ship.OrbitEstablished); Assert.False(ship.RouteActive); Assert.True(ship.Velocity.Length > 100);
        Assert.True(world.SetRoute(ship.Id, new(2, Array.Empty<NavigationPoint>(), false)));
        Assert.Equal(0u, ship.OrbitTargetId); Assert.False(ship.OrbitEstablished);
    }

    [Fact]
    public void OrbitTransferLeavesStationAndEstablishesTheRequestedSurfaceAltitude()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101)); var ship = world.AddPlayer("Transfer pilot");
        var body = world.Catalog.Planet;
        Assert.True(world.StartOrbit(ship.Id, new(1, (uint)body.Id, 500000)).Accepted);
        var boost = false; var cruise = false; var warp = false;
        for (var i = 0; i < 10000 && !ship.OrbitEstablished && ship.OrbitTargetId != 0; i++)
        {
            world.ApplyInput(ship.Id, InputState.None); world.Tick(.05);
            boost |= ship.Boosting; cruise |= ship.Cruising; warp |= ship.InWarp;
        }
        Assert.True(boost); Assert.True(cruise); Assert.True(warp);
        Assert.True(ship.OrbitEstablished, $"Orbit lost or not established at {ship.Position}, route {ship.RouteActive}/{ship.RouteIndex}, warp {ship.WarpStage}");
        Assert.InRange(Vec3d.Distance(ship.Position, world.Catalog.CelestialOffset(body)) - body.Radius, 499980, 500020);
    }

    [Fact]
    public void CustomWarpUsesTheRealStationShellAndRejectsUnsafeCelestialDistance()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101)); var ship = world.AddPlayer("Pilot");
        ship.Position = new Vec3d(0, 100000, 0); var before = ship.Position;
        Assert.False(world.Warp(ship.Id, (uint)world.Catalog.Star.Id, surfaceDistance: 500).Success);
        Assert.False(world.Warp(ship.Id, (uint)world.Catalog.Planet.Id, surfaceDistance: 500).Success);
        Assert.False(world.Warp(ship.Id, (uint)world.Catalog.Station.Id, surfaceDistance: 199).Success);
        Assert.False(world.Warp(ship.Id, (uint)world.Catalog.Station.Id, surfaceDistance: double.NaN).Success);
        Assert.Equal(before, ship.Position);
        var result = world.Warp(ship.Id, (uint)world.Catalog.Station.Id, surfaceDistance: 500);
        Assert.True(result.Success, result.Message);
        var surface = WorldSurfaces.Body(world.Catalog, world.Catalog.Station, WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds);
        Assert.InRange(surface.Distance(result.Destination), 499.999, 500.001);
        for (var i = 0; i < 160 && ship.InWarp; i++) world.Tick(.05);
        Assert.Equal(result.Destination, ship.Position); Assert.False(ship.InWarp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomStationArrivalStillChecksOtherShipsAtChargeAndExit(bool duringCharge)
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Pilot");
        ship.Position = new Vec3d(0, 100000, 0);
        var before = ship.Position;
        var result = world.Warp(ship.Id, (uint)world.Catalog.Station.Id, surfaceDistance: 500);
        Assert.True(result.Success, result.Message);
        if (!duringCharge) for (var i = 0; i < 50; i++) world.Tick(.05);
        var blocker = world.AddPlayer("Blocker");
        blocker.Position = result.Destination;
        for (var i = 0; i < 160 && ship.InWarp; i++) world.Tick(.05);
        Assert.False(ship.InWarp);
        Assert.Equal(before, ship.Position);
        Assert.True(Vec3d.Distance(ship.Position, blocker.Position) > WarpFlight.Clearance);
    }

    [Fact]
    public void FreePointCannotBypassStarSafetyAndCustomOffsetIsApplied()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101)); var ship = world.AddPlayer("Pilot");
        ship.Position = new Vec3d(0, 100000, 0);
        var star = world.Catalog.Star;
        var unsafePoint = world.Catalog.CelestialOffset(star) + new Vec3d(star.Radius + 5000, 0, 0);
        Assert.False(world.Warp(ship.Id, 0, unsafePoint).Success);
        var point = ship.Position + new Vec3d(100000, 0, 0);
        var result = world.Warp(ship.Id, 0, point, surfaceDistance: 500);
        Assert.True(result.Success, result.Message); Assert.Equal(500, Vec3d.Distance(point, result.Destination), 3);
    }

    [Fact]
    public void OrbitAndDistanceCommandsRoundTripOnTheWire()
    {
        foreach (var message in new IProtocolMessage[]{new OrbitRequestMessage(1,2,500000,true),
            new OrbitResultMessage(1,2,OrbitError.UnsafeAltitude,10000),new WarpRequestMessage(3,2,SurfaceDistance:500)})
        {
            Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.Encode(message), out var copy, out var error), error);
            Assert.Equal(message, copy);
        }
    }
}
