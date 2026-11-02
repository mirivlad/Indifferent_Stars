using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class CruiseTests
{
    private static InputState Cruise => new(1, 0, 0, 0, false, false, Cruise: true);

    [Fact]
    public void ProtectiveCruiseBrakeKeepsItsDecelerationThroughTheFinalSlowPhase()
    {
        var ship = new ShipState { Id = 1, Name = "Safety", IsPlayer = true, Velocity = new Vec3d(600, 0, 0), CruiseInhibited = true };
        for (var i = 0; i < 4; i++) FlightModel.StepPlayer(ship, InputState.None with { Brake = true }, 0.05);
        Assert.Equal(Vec3d.Zero, ship.Velocity);
        Assert.True(ship.Position.X <= 600 * 600 / (2 * FlightModel.CruiseBrakeAcceleration));
    }

    [Fact]
    public void CruiseAcceleratesToLimitThenBrakesWithEngines()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Cruise");
        ship.Position = new Vec3d(200_000, 0, 0);
        ship.SetAngles(-System.Math.PI / 2, 0);
        for (var i = 0; i < 240; i++) { world.ApplyInput(ship.Id, Cruise); world.Tick(0.05); }
        Assert.True(ship.Cruising);
        Assert.InRange(ship.Velocity.Length, 19_999, WorldCatalog.CruiseSpeed + 0.01);
        var speed = ship.Velocity.Length;
        world.ApplyInput(ship.Id, InputState.None);
        world.Tick(0.05);
        Assert.Equal(speed, ship.Velocity.Length, 5);
        for (var i = 0; i < 180; i++) { world.ApplyInput(ship.Id, InputState.None with { Brake = true }); world.Tick(0.05); }
        Assert.Equal(0, ship.Velocity.Length, 5);
    }

    [Fact]
    public void CruiseIsDeniedNearStationAndAutomaticallyBrakesOnApproach()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Safety");
        world.ApplyInput(ship.Id, Cruise); world.Tick(0.05);
        Assert.False(ship.Cruising);
        Assert.Equal(0, ship.Velocity.Length, 5);
        world.ApplyInput(ship.Id, InputState.None); world.Tick(0.05);
        ship.Position = new Vec3d(120_000, 0, 0);
        ship.SetAngles(System.Math.PI / 2, 0);
        ship.Velocity = new Vec3d(-20_000, 0, 0);
        var minimum = double.MaxValue;
        for (var i = 0; i < 240; i++)
        {
            world.ApplyInput(ship.Id, Cruise); world.Tick(0.05);
            minimum = System.Math.Min(minimum, ship.Position.Length - world.Catalog.Station.Radius);
        }
        Assert.True(minimum >= WorldCatalog.CruiseClearance);
        Assert.False(ship.Cruising);
        Assert.True(ship.CruiseInhibited);
        Assert.Equal(0, ship.Velocity.Length, 5);
    }

    [Fact]
    public void SystemWarpRequiresCruiseAndStopsTenKmAboveAtmosphere()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Warp");
        Assert.False(world.Warp(ship.Id, (uint)catalog.Planet.Id, 10000).Success);
        ship.Position = new Vec3d(200_000, 0, 0);
        for (var i = 0; i < 20; i++) { world.ApplyInput(ship.Id, Cruise); world.Tick(0.05); }
        var result = world.Warp(ship.Id, (uint)catalog.Planet.Id, 10000);
        Assert.True(result.Success, result.Message);
        Assert.Equal(10000, Vec3d.Distance(catalog.LocalFrameOrigin + ship.Position, catalog.Planet.SystemPosition)
            - catalog.Planet.Radius - catalog.Planet.AtmosphereHeight, 4);
        Assert.Equal(Vec3d.Zero, ship.Velocity);
        Assert.False(ship.Cruising);
        Assert.False(world.Warp(ship.Id, (uint)catalog.Star.Id, 10000).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemWarpReachesStationAndStarWithoutOldRangeLimit(bool star)
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Traveller");
        ship.Position = new Vec3d(200_000, 0, 0);
        for (var i = 0; i < 20; i++) { world.ApplyInput(ship.Id, Cruise); world.Tick(0.05); }
        var target = star ? catalog.Star.SystemPosition - catalog.LocalFrameOrigin : catalog.Station.Position;
        var radius = star ? catalog.Star.Radius + catalog.Star.AtmosphereHeight : catalog.Station.Radius;
        var result = world.Warp(ship.Id, (uint)(star ? catalog.Star.Id : catalog.Station.Id), 10000);
        Assert.True(result.Success, result.Message);
        Assert.Equal(10000, Vec3d.Distance(ship.Position, target) - radius, 3);
        world.Tick(0.05);
        Assert.Equal(Vec3d.Zero, ship.Velocity);
        Assert.Equal(result.NewPosition, ship.Position);
    }

    [Fact]
    public void SystemWarpCannotUseHiddenShipOrOccupiedArrival()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Traveller");
        var other = world.AddPlayer("Hidden");
        ship.Position = new Vec3d(200_000, 0, 0);
        for (var i = 0; i < 20; i++) { world.ApplyInput(ship.Id, Cruise); world.Tick(0.05); }
        Assert.False(world.Warp(ship.Id, other.Id, 10000).Success);
        var position = ship.Position;
        var velocity = ship.Velocity;
        var away = (position - catalog.Station.Position).Normalized();
        other.Position = catalog.Station.Position + away * (catalog.Station.Radius + 10000);
        Assert.False(world.Warp(ship.Id, (uint)catalog.Station.Id, 10000).Success);
        Assert.Equal(position, ship.Position);
        Assert.Equal(velocity, ship.Velocity);
    }
}
