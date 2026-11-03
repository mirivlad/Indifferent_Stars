using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class ImpulseTests
{
    private static ShipState Ship(uint id,Vec3d position,Vec3d velocity)=>new() {Id=id,Name="Impact",IsPlayer=true,Position=position,Velocity=velocity};
    [Fact]
    public void CenteredEqualMassImpactConservesMomentumAndDissipatesEnergy()
    {
        var a=Ship(1,new(-5,0,0),new(100,0,0));var b=Ship(2,new(5,0,0),new(-100,0,0));
        var initial=a.Velocity*a.MassKg+b.Velocity*b.MassKg;
        CollisionSolver.Resolve(a,b,Vec3d.Zero,new(-1,0,0),Vec3d.Zero);
        Assert.InRange(a.Velocity.X,-18.0001,-17.9999);Assert.InRange(b.Velocity.X,17.9999,18.0001);
        Assert.True((a.Velocity*a.MassKg+b.Velocity*b.MassKg-initial).Length<1e-6);
        Assert.True(a.Velocity.LengthSquared+b.Velocity.LengthSquared<20000);
    }
    [Fact]
    public void GlancingImpactKeepsSlidingAndAddsSpin()
    {
        var a=Ship(1,new(0,5,0),new(100,-20,0));
        var hit=CollisionSolver.Resolve(a,null,Vec3d.Zero,Vec3d.Up,Vec3d.Zero);
        Assert.Equal(20,hit.NormalSpeed);Assert.Equal(100,hit.TangentialSpeed);
        Assert.InRange(a.Velocity.X,1,99.99);Assert.True(a.Velocity.Y>0);
        Assert.True(a.AngularVelocity.Length>0);Assert.True(a.AngularVelocity.Length<=2.00001);
    }
    [Fact]
    public void SeparatingContactDoesNotStopOrReflectAgain()
    {
        var a=Ship(1,new(0,5,0),new(7,20,0));var velocity=a.Velocity;
        Assert.Equal(0,CollisionSolver.Resolve(a,null,Vec3d.Zero,Vec3d.Up,Vec3d.Zero).NormalSpeed);
        Assert.Equal(velocity,a.Velocity);
    }
    [Fact]
    public void DockedShipIsAnImmovableBody()
    {
        var a=Ship(1,new(-5,0,0),new(100,0,0));var b=Ship(2,new(5,0,0),Vec3d.Zero);b.Docked=true;
        CollisionSolver.Resolve(a,b,Vec3d.Zero,new(-1,0,0),Vec3d.Zero);
        Assert.True(a.Velocity.X<0);Assert.Equal(Vec3d.Zero,b.Velocity);Assert.Equal(Vec3d.Zero,b.AngularVelocity);
    }
    [Theory]
    [InlineData(.000001)]
    [InlineData(.0001)]
    [InlineData(.01)]
    public void SmallAngularMotionSurvivesFloatQuaternionRounding(double angle)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)angle);
        var motion=SurfaceContact.AngularMotion(Quaternion.Identity,rotation);
        Assert.InRange(motion.Y,angle-1e-8,angle+1e-8);
    }
    [Fact]
    public void DetailedContactProvidesSurfacePointAndOutwardNormal()
    {
        var hit=SurfaceContact.SweepHit(SurfaceMesh.Ship,1,new(-100,0,0),new(100,0,0),Quaternion.Identity,Quaternion.Identity,
            SurfaceMesh.Ship,1,new(100,0,0),new(-100,0,0),Quaternion.Identity,Quaternion.Identity);
        Assert.NotNull(hit);Assert.InRange(hit.Value.Time,.47,.49);
        Assert.InRange(hit.Value.Normal.Length,.999999,1.000001);Assert.True(hit.Value.Normal.X<-.5,$"Contact: {hit.Value}");
        Assert.True(hit.Value.Point.IsFinite);
    }
}
