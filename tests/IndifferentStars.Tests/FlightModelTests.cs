using System;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
using Xunit;

namespace IndifferentStars.Tests;

public sealed class FlightModelTests
{
    private static GameWorld NewWorld() => new(WorldCatalog.Generate(12345));

    [Fact]
    public void FixedStepSimulationIsDeterministic()
    {
        var a = NewWorld();
        var b = NewWorld();
        var shipA = a.AddPlayer("A");
        var shipB = b.AddPlayer("B");

        var inputs = new[]
        {
            new InputState(1f, 0.5f, 0.2f, 0f, false, false),
            new InputState(1f, -0.4f, -0.6f, 0.5f, true, false),
            new InputState(0.2f, 0f, 0.8f, -0.3f, false, true),
            InputState.None,
        };

        for (var i = 0; i < 400; i++)
        {
            a.ApplyInput(shipA.Id, inputs[(i / 50) % inputs.Length]);
            b.ApplyInput(shipB.Id, inputs[(i / 50) % inputs.Length]);
            a.Tick(0.05);
            b.Tick(0.05);
        }

        Assert.Equal(shipA.Position.X, shipB.Position.X, 10);
        Assert.Equal(shipA.Position.Y, shipB.Position.Y, 10);
        Assert.Equal(shipA.Position.Z, shipB.Position.Z, 10);
        Assert.Equal(shipA.Yaw, shipB.Yaw, 10);
        Assert.Equal(shipA.Pitch, shipB.Pitch, 10);
        Assert.Equal(shipA.Velocity.X, shipB.Velocity.X, 10);
    }

    [Fact]
    public void ShipRespectsBoostSpeedLimit()
    {
        var world = NewWorld();
        var ship = world.AddPlayer("Racer");
        ship.Position = Vec3d.Zero;
        ship.Yaw = Math.PI / 2;
        ship.Pitch = 0;

        for (var i = 0; i < 120; i++)
        {
            world.ApplyInput(ship.Id, new InputState(1f, 0, 0, 0, true, false));
            world.Tick(0.05);
        }

        Assert.True(ship.Velocity.Length <= WorldCatalog.MaxBoostSpeed + 0.5,
            $"Speed {ship.Velocity.Length:F2} exceeded boost limit.");
        Assert.True(ship.Velocity.Length > WorldCatalog.MaxSpeed,
            "Ship should exceed normal speed while boosting.");
    }

    [Fact]
    public void InputExpiresAndShipCoastsToRest()
    {
        var world = NewWorld();
        var ship = world.AddPlayer("Drifter");

        for (var i = 0; i < 100; i++)
        {
            world.ApplyInput(ship.Id, new InputState(1f, 0, 0, 0, false, false));
            world.Tick(0.05);
        }

        Assert.True(ship.Velocity.Length > 50);

        for (var i = 0; i < 400; i++)
            world.Tick(0.05);

        Assert.True(ship.Velocity.Length < 1.0, $"Ship did not coast down, speed={ship.Velocity.Length:F2}");
    }

    [Theory]
    [InlineData(8500)]
    [InlineData(10000)]
    [InlineData(50000)]
    [InlineData(100000)]
    public void PlayerContinuesOutwardPastFormerBoundary(double boundary)
    {
        var world = NewWorld();
        var ship = world.AddPlayer("Voyager");
        ship.Position = new Vec3d(boundary - 100, 0, 0);
        ship.Yaw = -Math.PI / 2; // forward = +X, away from origin

        for (var i = 0; i < 100; i++)
        {
            world.ApplyInput(ship.Id, new InputState(1f, 0, 0, 0, true, false));
            world.Tick(0.05);
        }

        Assert.True(ship.Position.X > boundary, $"Ship did not pass {boundary:F0} m: {ship.Position.X:F1}");
        Assert.True(ship.Velocity.X > 0, "Outward velocity was removed.");
    }

    [Fact]
    public void YawAndPitchProduceUnitBasis()
    {
        var (forward, right, up) = FlightModel.Basis(0.7, 0.4);
        Assert.Equal(1.0, forward.Length, 6);
        Assert.Equal(1.0, right.Length, 6);
        Assert.Equal(1.0, up.Length, 6);
        Assert.Equal(0.0, Vec3d.Dot(forward, right), 6);
        Assert.Equal(0.0, Vec3d.Dot(forward, up), 6);
        Assert.Equal(0.0, Vec3d.Dot(right, up), 6);
    }
}
