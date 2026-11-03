using IndifferentStars.Shared.Protocol;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class SurfaceCollisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroAreaFacesAreNotSolidSurfaces(bool reverse)
    {
        var point = new Triangle(new(-100,0,0),new(-100,0,0),new(-100,0,0));
        var face = new Triangle(new(100,0,0),new(100,1,0),new(100,0,1));
        var narrowPhase = typeof(SurfaceContact).GetMethod("Triangles",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var result = narrowPhase.Invoke(null, new object[]
        {
            reverse ? face : point, reverse ? point : face,
            new Vec3d(reverse ? -200 : 200,0,0), 0d, 0d, 1d,
        });
        Assert.Null(result);
    }

    [Fact]
    public void OpposingDetailedHullsMeetAtTheShellRatherThanAtTheSweepStart()
    {
        var contact = SurfaceContact.Sweep(SurfaceMesh.Ship, 1,
            new Vec3d(-100,0,0), new Vec3d(100,0,0), Quaternion.Identity, Quaternion.Identity,
            SurfaceMesh.Ship, 1, new Vec3d(100,0,0), new Vec3d(-100,0,0), Quaternion.Identity, Quaternion.Identity);
        Assert.NotNull(contact);
        Assert.InRange(contact.Value, .47, .49);
    }

    [Fact]
    public void MovingObstacleCannotBeCulledUsingOnlyItsStartingDistance()
    {
        Assert.NotNull(SurfaceContact.Sweep(SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero,
            Quaternion.Identity, Quaternion.Identity, SurfaceMesh.Ship, 1,
            new Vec3d(100,0,0), new Vec3d(-100,0,0), Quaternion.Identity, Quaternion.Identity));
    }

    [Fact]
    public void RotatingObstacleCanSweepIntoASmallStationaryHull()
    {
        var position = new Vec3d(4.2,2,1.9);
        double? Sweep(Quaternion rotation) => SurfaceContact.Sweep(SurfaceMesh.Ship, .1,
            position, position, Quaternion.Identity, Quaternion.Identity,
            SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero, Quaternion.Identity, rotation);
        Assert.Null(Sweep(Quaternion.Identity));
        Assert.NotNull(Sweep(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI / 2)));
    }

    private static double? StationSweep(double x) => SurfaceContact.Sweep(
        SurfaceMesh.Ship, 1, new Vec3d(x, 100, 0), new Vec3d(x, -100, 0), Quaternion.Identity, Quaternion.Identity,
        SurfaceMesh.Station, 55, Vec3d.Zero, Vec3d.Zero, Quaternion.Identity, Quaternion.Identity);

    [Fact]
    public void RingOpeningIsEmptyButRimAndSolarPanelAreSolid()
    {
        Assert.Null(StationSweep(110));
        Assert.NotNull(StationSweep(160));
        Assert.NotNull(StationSweep(308));
        Assert.Null(StationSweep(450));
    }

    [Fact]
    public void NacellesCollideButEmptySpaceBetweenParallelShipsDoesNot()
    {
        double? Sweep(double x) => SurfaceContact.Sweep(
            SurfaceMesh.Ship, 1, new Vec3d(x, 0, 20), new Vec3d(x, 0, -20), Quaternion.Identity, Quaternion.Identity,
            SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero, Quaternion.Identity, Quaternion.Identity);
        Assert.Null(Sweep(12));
        Assert.NotNull(Sweep(8));
    }

    [Fact]
    public void RotationAloneCanBringANacelleIntoContact()
    {
        var above = new Vec3d(0, 5.6, 0);
        Assert.Null(SurfaceContact.Sweep(SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero,
            Quaternion.Identity, Quaternion.Identity, SurfaceMesh.Ship, 1, above, above,
            Quaternion.Identity, Quaternion.Identity));
        Assert.NotNull(SurfaceContact.Sweep(SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero,
            Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)System.Math.PI / 2),
            SurfaceMesh.Ship, 1, above, above, Quaternion.Identity, Quaternion.Identity));
    }

    [Fact]
    public void StationSurfaceDistanceDistinguishesTheHoleFromSolidMetal()
    {
        Assert.True(SurfaceQuery.Distance(SurfaceMesh.Station, new Vec3d(2, 0, 0)) > 0.3);
        Assert.False(SurfaceQuery.Contains(SurfaceMesh.Station, new Vec3d(2, 0, 0)));
        Assert.True(SurfaceQuery.Contains(SurfaceMesh.Station, new Vec3d(0, 0, 0)));
    }

    [Fact]
    public void FreighterRouteDoesNotEndInsideTheStation()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var station = WorldSurfaces.Body(world.Catalog, world.Catalog.Station, 0);
        var couriers=world.Npcs.Where(npc=>npc.Work?.Occupation==NpcOccupation.Courier).ToArray();
        Assert.NotEmpty(couriers);
        Assert.All(couriers,ship=>Assert.True(station.Distance(ship.Position)>SurfaceMesh.Ship.Radius));
    }

    [Fact]
    public void SurfaceMatchesTheResourceRatherThanCatalogRadius()
    {
        Assert.True(SurfaceMesh.Station.Radius * ModelPose.StationScale > 380);
        Assert.InRange(SurfaceMesh.Ship.Radius, 11, 13);
        Assert.Equal(64, SurfaceMesh.Station.SourceHash.Length);
    }
}
