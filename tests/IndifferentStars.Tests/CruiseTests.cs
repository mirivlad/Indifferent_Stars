using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class CruiseTests
{
    [Fact]
    public void RefusedCruiseDoesNotBlockOrdinaryThrust()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Safety");
        ship.Velocity = ship.Forward * 100;
        for (var i = 0; i < 20; i++)
        {
            world.ApplyInput(ship.Id, new InputState(1, 0, 0, 0, false, false, Cruise: true));
            world.Tick(0.05);
        }
        Assert.False(ship.Cruising);
        Assert.True(ship.CruiseInhibited);
        Assert.True(ship.Velocity.Length > 200, "A refused cruise request must not keep braking ordinary thrust.");
    }

    [Fact]
    public void ProtectiveBrakeCompletesAfterCruiseRequestIsReleased()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Safety");
        ship.Position = new Vec3d(80_000, 0, 0);
        ship.SetAngles(System.Math.PI / 2, 0);
        ship.Velocity = new Vec3d(-20_000, 0, 0);
        for (var i = 0; i < 40 && !ship.CruiseInhibited; i++)
        {
            world.ApplyInput(ship.Id, Cruise); world.Tick(0.05);
        }
        Assert.True(ship.CruiseInhibited);
        for (var i = 0; i < 200; i++)
        {
            world.ApplyInput(ship.Id, InputState.None); world.Tick(0.05);
        }
        Assert.Equal(Vec3d.Zero, ship.Velocity);
        Assert.True(ship.Position.Length - world.Catalog.Station.Radius >= WorldCatalog.CruiseClearance);
        world.ApplyInput(ship.Id, InputState.None with { Throttle = 1 }); world.Tick(0.05);
        Assert.True(ship.Velocity.Length > 0);
    }

    [Fact]
    public void CruiseCanBeRequestedAgainAfterRefusalAndDeparture()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Safety");
        ship.Velocity = ship.Forward * 200;
        world.ApplyInput(ship.Id, Cruise); world.Tick(0.05);
        Assert.True(ship.CruiseInhibited);
        Assert.False(ship.CruiseBraking);
        Assert.Equal(200, ship.Velocity.Length, 3);
        world.ApplyInput(ship.Id, InputState.None); world.Tick(0.05);
        Assert.False(ship.CruiseInhibited);
        ship.Position = new Vec3d(200_000, 0, 0);
        ship.SetAngles(-System.Math.PI / 2, 0);
        for (var i = 0; i < 240; i++) { world.ApplyInput(ship.Id, Cruise); world.Tick(0.05); }
        Assert.True(ship.Cruising);
        Assert.InRange(ship.Velocity.Length, 19_999, WorldCatalog.CruiseSpeed + 0.01);
    }

    private static InputState Cruise => new(0, 0, 0, 0, false, false, Cruise: true);

    [Fact]
    public void ProtectiveCruiseBrakeKeepsItsDecelerationThroughTheFinalSlowPhase()
    {
        var ship = new ShipState { Id = 1, Name = "Safety", IsPlayer = true, Velocity = new Vec3d(600, 0, 0), CruiseBraking = true };
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
        for (var i = 0; i < 400; i++) { world.ApplyInput(ship.Id, InputState.None with { Brake = true }); world.Tick(0.05); }
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

}
