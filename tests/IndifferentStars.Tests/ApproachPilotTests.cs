using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class ApproachPilotTests
{
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
}
