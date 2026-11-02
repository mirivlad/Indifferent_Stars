using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class ApproachPilotTests
{
    [Fact]
    public void WaypointDepartureAllowsCruiseWithoutManualPiloting()
    {
        var catalog = WorldCatalog.Generate(20261101);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Captain");
        var start = ship.Position;
        var destination = new Vec3d(-30_000, ship.Position.Y, 0);
        Assert.True(world.StartMove(ship.Id, destination));
        Assert.Equal(start, ship.Position);
        Assert.Equal(destination, world.BuildSnapshot().Single(entity => entity.Id == ship.Id).ApproachPoint);
        for (var i = 0; i < 6000 && ship.ApproachTargetId != 0; i++)
        {
            world.ApplyInput(ship.Id, InputState.None);
            world.Tick(0.05);
        }
        Assert.Equal(0u, ship.ApproachTargetId);
        Assert.True(ApproachPilot.Arrived(ship, destination));
        for (var i = 0; i < 20; i++)
        {
            world.ApplyInput(ship.Id, InputState.None with { Throttle = 1, Cruise = true });
            world.Tick(0.05);
        }
        Assert.True(ship.Cruising);
        Assert.True(world.Warp(ship.Id, (uint)catalog.Planet.Id, 10000).Success);
    }

    [Fact]
    public void InvalidWaypointCannotReplaceOrderOrTeleportPlayer()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Captain");
        var position = ship.Position;
        var destination = position + new Vec3d(5000, 0, 0);
        Assert.True(world.StartMove(ship.Id, destination));
        Assert.False(world.StartMove(ship.Id, new Vec3d(double.NaN, 0, 0)));
        Assert.False(world.StartMove(ship.Id, new Vec3d(double.MaxValue, 0, 0)));
        Assert.Equal(destination, ship.ApproachDestination);
        Assert.Equal(position, ship.Position);
        world.ApplyInput(ship.Id, InputState.None with { Strafe = 1 });
        Assert.Equal(0u, ship.ApproachTargetId);
        ship.Docked = true;
        Assert.False(world.StartMove(ship.Id, destination));
    }

    [Theory]
    [InlineData(1000, 200, -2500)]
    [InlineData(0, 0, 2000)]
    public void ApproachesAndStopsUsingFlightModel(double x, double y, double z)
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var ship = world.AddPlayer("Pilot");
        ship.Position = Vec3d.Zero;
        ship.SetAngles(0, 0);
        ship.Velocity = new Vec3d(35, -12, 15);
        var destination = new Vec3d(x, y, z);
        var arrived = false;
        for (var i = 0; i < 6000; i++)
        {
            FlightModel.StepPlayer(ship, ApproachPilot.Steer(ship, destination), 0.05);
            if (ApproachPilot.Arrived(ship, destination))
            {
                arrived = true;
                break;
            }
        }
        Assert.True(arrived, $"No arrival: distance {Vec3d.Distance(ship.Position, destination):F1}, speed {ship.Velocity.Length:F1}");
    }

    [Fact]
    public void WorldApproachArrivesAndManualInputCancels()
    {
        var catalog = WorldCatalog.Generate(12345);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Pilot");
        Assert.False(world.StartApproach(ship.Id, uint.MaxValue));
        Assert.False(world.StartApproach(uint.MaxValue, (uint)catalog.Station.Id));
        Assert.True(world.StartApproach(ship.Id, (uint)catalog.Station.Id));
        var destination = ship.ApproachDestination;
        for (var i = 0; i < 6000 && ship.ApproachTargetId != 0; i++)
        {
            world.ApplyInput(ship.Id, InputState.None);
            world.Tick(0.05);
        }
        Assert.Equal(0u, ship.ApproachTargetId);
        Assert.True(ApproachPilot.Arrived(ship, destination));
        Assert.True(world.StartApproach(ship.Id, (uint)catalog.Station.Id));
        world.ApplyInput(ship.Id, new InputState(0, 0, 0, 0, false, false, 1, 0));
        Assert.Equal(0u, ship.ApproachTargetId);
    }

    [Fact]
    public void FollowTracksMovingShipAndStopsWhenTargetLeaves()
    {
        var world = new GameWorld(WorldCatalog.Generate(12345));
        var follower = world.AddPlayer("Follower");
        var leader = world.AddPlayer("Leader");
        follower.Position = new Vec3d(0, 0, 0);
        leader.Position = new Vec3d(2000, 0, 0);
        leader.Velocity = new Vec3d(50, 0, 0);
        Assert.NotEqual((uint)world.Catalog.Station.Id, follower.Id);
        Assert.False(world.StartApproach(follower.Id, follower.Id, true));
        Assert.True(world.StartApproach(follower.Id, leader.Id, true));
        for (var i = 0; i < 2000; i++)
        {
            world.ApplyInput(follower.Id, InputState.None);
            world.ApplyInput(leader.Id, InputState.None);
            world.Tick(0.05);
        }
        Assert.Equal(leader.Id, follower.ApproachTargetId);
        Assert.InRange(Vec3d.Distance(follower.Position, leader.Position), 490, 550);
        Assert.InRange(Vec3d.Distance(follower.Velocity, leader.Velocity), 0, 3);
        world.RemovePlayer(leader.Id);
        world.Tick(0.05);
        Assert.Equal(0u, follower.ApproachTargetId);
    }
}
