using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
namespace IndifferentStars.Tests;
public sealed class HighSpeedTests
{
    [Theory]
    [InlineData(100_000)] [InlineData(200_000)] [InlineData(500_000)]
    public void BrakingDistanceUsesActualSpeedAndFiniteAcceleration(double speed)
    {
        var ship=new ShipState {Id=1,Name="Brake",IsPlayer=true,Velocity=new Vec3d(speed,0,0)};
        var distance=speed*speed/(2*FlightModel.CruiseBrakeAcceleration);
        while(ship.Velocity.Length>0) FlightModel.StepPlayer(ship,InputState.None with {Brake=true},.05);
        Assert.InRange(ship.Position.X,distance-speed*.05-1,distance+1);
    }
    [Theory]
    [InlineData(100_000)] [InlineData(200_000)] [InlineData(500_000)]
    public void WholeTickSweepHitsDetailedStationShipAndAsteroid(double speed)
    {
        var half=speed*.025; var rotation=Quaternion.Identity;
        double? Hit(SurfaceMesh mesh,double scale,Vec3d position,Vec3d direction) =>
            SurfaceContact.Sweep(SurfaceMesh.Ship,1,position-direction*half,position+direction*half,rotation,rotation,
                mesh,scale,Vec3d.Zero,Vec3d.Zero,rotation,rotation);
        Assert.NotNull(Hit(SurfaceMesh.Station,ModelPose.StationScale,Vec3d.Zero,Vec3d.Up));
        Assert.NotNull(Hit(SurfaceMesh.Ship,1,Vec3d.Zero,new Vec3d(1,0,0)));
        var world=new GameWorld(WorldCatalog.Generate(20261101));var body=world.Catalog.Asteroids[0];
        var surface=WorldSurfaces.Body(world.Catalog,body,0);
        Assert.NotNull(WorldSurfaces.ContactHit(
            new SurfacePose(SurfaceMesh.Ship,1,body.Position-Vec3d.Up*half,rotation),
            new SurfacePose(SurfaceMesh.Ship,1,body.Position+Vec3d.Up*half,rotation),surface,surface));
    }
    [Theory]
    [InlineData(100_000)] [InlineData(200_000)] [InlineData(500_000)]
    public void ServerCannotCrossPlanetInOneTick(double speed)
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var ship=world.AddPlayer("Planet CCD");
        var planet=world.Catalog.Planet;var center=world.Catalog.CelestialOffset(planet);
        ship.Position=center+Vec3d.Up*(planet.Radius+speed*.025);
        ship.Velocity=-Vec3d.Up*speed;
        world.Tick(.05);
        Assert.True(Vec3d.Distance(ship.Position,center)>=planet.Radius);
        Assert.NotNull(ship.Impact);
    }
    [Fact]
    public void PopulationLimitSlowsCruiseGraduallyAndOpenSpaceRetainsFullSpeed()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var ship=world.AddPlayer("Cruise");
        ship.Position=new Vec3d(2_000_000,0,0);ship.Velocity=ship.Forward*500_000;
        ship.CruiseSpeedLimit=world.CruiseLimitAt(ship);
        Assert.Equal(150_000,ship.CruiseSpeedLimit);
        FlightModel.StepPlayer(ship,InputState.None with {Cruise=true},.05);
        Assert.InRange(ship.Velocity.Length,498_499,498_501);
        ship.Position=new Vec3d(100_000_000,0,0);
        Assert.Equal(500_000,world.CruiseLimitAt(ship));
    }
}
