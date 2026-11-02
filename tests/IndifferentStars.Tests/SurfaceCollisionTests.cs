using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class SurfaceCollisionTests
{
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
    public void WingTipsCollideButEmptySpaceBetweenParallelShipsDoesNot()
    {
        double? Sweep(double x) => SurfaceContact.Sweep(
            SurfaceMesh.Ship, 1, new Vec3d(x, 0, 20), new Vec3d(x, 0, -20), Quaternion.Identity, Quaternion.Identity,
            SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero, Quaternion.Identity, Quaternion.Identity);
        Assert.Null(Sweep(12));
        Assert.NotNull(Sweep(8));
    }

    [Fact]
    public void RotationAloneCanBringAWingIntoContact()
    {
        var above = new Vec3d(0, 4, 0);
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
        var freighter = world.Npcs.Single(npc => npc.Name == "Long Cargo");
        Assert.All(freighter.Route!.Waypoints, point => Assert.True(station.Distance(point) > SurfaceMesh.Ship.Radius));
    }

    [Fact]
    public void SurfaceMatchesTheResourceRatherThanCatalogRadius()
    {
        Assert.True(SurfaceMesh.Station.Radius * ModelPose.StationScale > 380);
        Assert.InRange(SurfaceMesh.Ship.Radius, 4, 8);
        Assert.Equal(64, SurfaceMesh.Station.SourceHash.Length);
    }
}
