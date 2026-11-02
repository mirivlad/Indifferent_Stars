using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class DockingTests
{
    [Fact]
    public void ServiceBerthIsFixedOnTheSpinAxisAndClearOfTheHull()
    {
        var catalog = WorldCatalog.Generate(12345);
        var world = new GameWorld(catalog);
        var a = world.AddPlayer("Port");
        var b = world.AddPlayer("Starboard");
        b.Position = new Vec3d(-500, 400, 0);
        Assert.True(world.RequestDocking(a.Id, (uint)catalog.Station.Id));
        Assert.False(world.RequestDocking(b.Id, (uint)catalog.Station.Id));
        Assert.Equal(catalog.Station.Position + ModelPose.StationDockingOffset, a.DockingPoint);
        var point = a.DockingPoint;
        Assert.Equal<Vec3d?>(point, world.BuildSnapshot(a.Id).Single(entity => entity.Id == a.Id).ApproachPoint);
        Assert.True(world.StartMove(a.Id, new Vec3d(1000, 500, 0)));
        Assert.True(world.RequestDocking(b.Id, (uint)catalog.Station.Id));
        Assert.Equal(point, b.DockingPoint);
        Assert.True(world.StartApproach(b.Id, (uint)catalog.Station.Id, false));
        Assert.True(world.RequestDocking(a.Id, (uint)catalog.Station.Id));
        a.Position = a.DockingPoint;
        a.Velocity = Vec3d.Zero;
        world.Tick(.05);
        Assert.True(a.Docked);
        Assert.False(world.RequestDocking(b.Id, (uint)catalog.Station.Id));
        Assert.True(world.Undock(a.Id));
        Assert.True(world.RequestDocking(b.Id, (uint)catalog.Station.Id));
        foreach (var seconds in new[] { 0.0, 60.0, 400.0 })
            Assert.True(WorldSurfaces.Body(catalog, catalog.Station, seconds).Distance(a.DockingPoint) > SurfaceMesh.Ship.Radius);
    }

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
