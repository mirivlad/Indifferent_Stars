using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class WarpSafetyTests
{
    private static (GameWorld World, ShipState Ship) Create()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Warp test");
        ship.Position = new Vec3d(0, 100_000, 0);
        return (world, ship);
    }

    private static void Finish(GameWorld world, ShipState ship)
    {
        for (var i = 0; i < 160 && ship.InWarp; i++) world.Tick(0.05);
        Assert.False(ship.InWarp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnyCelestialIsReachableWithoutCruiseOrRangeLimit(bool star)
    {
        var (world, ship) = Create();
        var target = star ? world.Catalog.Star : world.Catalog.Planet;
        var before = ship.Position;
        var result = world.Warp(ship.Id, (uint)target.Id);
        Assert.True(result.Success, result.Message);
        Assert.Equal(before, ship.Position);
        Assert.Equal(WarpStage.Charging, ship.WarpStage);
        Finish(world, ship);
        Assert.Equal(result.Destination, ship.Position);
        Assert.InRange(Vec3d.Distance(ship.Position, world.Catalog.CelestialOffset(target)) -
            target.Radius - target.AtmosphereHeight, 1000, 1010);
        Assert.Equal(Vec3d.Zero, ship.Velocity);
        Assert.False(ship.Cruising);
    }

    [Fact]
    public void StationArrivalUsesTheActualShellAndOneKmClearance()
    {
        var (world, ship) = Create();
        var result = world.Warp(ship.Id, (uint)world.Catalog.Station.Id);
        Assert.True(result.Success, result.Message);
        var atArrival = WorldSurfaces.Body(world.Catalog, world.Catalog.Station,
            WarpFlight.ChargeSeconds + WarpFlight.TravelSeconds);
        Assert.InRange(atArrival.Distance(result.Destination), 1000, 1010);
        Finish(world, ship);
        Assert.Equal(result.Destination, ship.Position);
    }

    [Fact]
    public void FreeWaypointAndConsecutiveFinishedWarpsNeedNoCooldown()
    {
        var (world, ship) = Create();
        var destination = new Vec3d(-1_000_000, 100_000, 5_000_000);
        Assert.True(world.Warp(ship.Id, 0, destination).Success);
        world.Tick(1);
        Assert.Equal(WarpStage.Charging, ship.WarpStage);
        Assert.NotEqual(destination, ship.Position);
        Assert.False(world.StartMove(ship.Id, destination));
        Assert.False(world.RequestDocking(ship.Id, (uint)world.Catalog.Station.Id));
        var before = ship.Position;
        world.ApplyInput(ship.Id, new InputState(1, 1, 1, 1, true, true));
        Assert.Equal(before, ship.Position);
        Finish(world, ship);
        Assert.Equal(destination, ship.Position);
        Assert.True(world.Warp(ship.Id, 0, new Vec3d(0, 100_000, 0)).Success);
    }

    [Fact]
    public void BusyUnsafeAndInvalidRequestsCannotMoveTheShip()
    {
        var (world, ship) = Create();
        var before = ship.Position;
        foreach (var point in new[]
        {
            Vec3d.Zero,
            world.Catalog.CelestialOffset(world.Catalog.Planet),
            new Vec3d(double.NaN, 0, 0),
            new Vec3d(double.PositiveInfinity, 0, 0),
            new Vec3d(double.MaxValue, 0, 0),
        })
        {
            Assert.False(world.Warp(ship.Id, 0, point).Success);
            Assert.Equal(before, ship.Position);
        }
        Assert.False(world.Warp(ship.Id, 3, new Vec3d(0, 100000, 0)).Success);
        var other = world.AddPlayer("Hidden");
        Assert.False(world.Warp(ship.Id, other.Id).Success);
        ship.Position = new Vec3d(0, 800, 0);
        Assert.False(world.Warp(ship.Id, 0, before).Success);
    }

    [Fact]
    public void NewlyOccupiedArrivalAbortsChargeOrRecoversSafely()
    {
        foreach (var duringCharge in new[] { true, false })
        {
            var (world, ship) = Create();
            var destination = new Vec3d(0, 200_000, 0);
            Assert.True(world.Warp(ship.Id, 0, destination).Success);
            if (!duringCharge)
                for (var i = 0; i < 50; i++) world.Tick(0.05);
            var blocker = world.AddPlayer("Blocker");
            blocker.Position = destination;
            Finish(world, ship);
            Assert.NotEqual(destination, ship.Position);
            Assert.True(Vec3d.Distance(ship.Position, blocker.Position) >= WarpFlight.Clearance);
            Assert.Equal(Vec3d.Zero, ship.Velocity);
        }
    }
}
