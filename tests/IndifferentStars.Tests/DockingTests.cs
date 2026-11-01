using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class DockingTests
{
    [Fact]
    public void DockingRequiresPermissionPositionAndLowSpeedAndUndockResumesFlight()
    {
        var catalog = WorldCatalog.Generate(12345);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Docking");
        Assert.False(world.RequestDocking(ship.Id, 1000));
        Assert.False(world.RequestDocking(uint.MaxValue, (uint)catalog.Station.Id));
        Assert.True(world.RequestDocking(ship.Id, (uint)catalog.Station.Id));
        var point = ship.DockingPoint;
        ship.Position = point;
        ship.Velocity = new Vec3d(20, 0, 0);
        world.ApplyInput(ship.Id, new InputState(0, 0, 0, 0, false, false, 1));
        world.Tick(0.05);
        Assert.False(ship.Docked);
        ship.Position = point + new Vec3d(100, 0, 0);
        ship.Velocity = Vec3d.Zero;
        world.Tick(0.05);
        Assert.False(ship.Docked);
        ship.Position = point;
        ship.Velocity = Vec3d.Zero;
        world.Tick(0.05);
        Assert.True(ship.Docked);
        Assert.False(world.Warp(ship.Id, (uint)catalog.Station.Id).Success);
        Assert.True(world.Undock(ship.Id));
        Assert.False(world.Undock(ship.Id));
        world.Tick(0.05);
        Assert.True(ship.Velocity.Length > 0);
    }

    [Fact]
    public void AutopilotCompletesDockingWithoutTeleport()
    {
        var catalog = WorldCatalog.Generate(12345);
        var world = new GameWorld(catalog);
        var ship = world.AddPlayer("Docking");
        Assert.True(world.RequestDocking(ship.Id, (uint)catalog.Station.Id));
        var previous = ship.Position;
        for (var i = 0; i < 6000 && !ship.Docked; i++)
        {
            world.ApplyInput(ship.Id, InputState.None);
            world.Tick(0.05);
            Assert.True(Vec3d.Distance(previous, ship.Position) < 20);
            previous = ship.Position;
        }
        Assert.True(ship.Docked);
        Assert.True(Vec3d.Distance(ship.Position, ship.DockingPoint) <= 15);
    }
}
