using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class NavigationRouteTests
{
    [Fact]
    public void PlanningAndEditingDoNotStartFlightAndManualInputStopsPilot()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Navigator");
        var point = new NavigationPoint(0, ship.Position + new Vec3d(0, 100, -500));
        var start = ship.Position;
        Assert.True(world.SetRoute(ship.Id, new(1, new[] { point }, false)));
        for (var i=0;i<10;i++)world.Tick(.05);
        Assert.Equal(start, ship.Position);
        Assert.False(ship.RouteActive);
        Assert.True(world.SetRoute(ship.Id, new(2, new[] { point }, true)));
        world.Tick(.05);
        Assert.True(ship.RouteActive);
        world.ApplyInput(ship.Id, InputState.None with { Strafe=1 });
        Assert.False(ship.RouteActive);
        Assert.Single(ship.NavigationRoute);
        Assert.True(world.SetRoute(ship.Id, new(3, Array.Empty<NavigationPoint>(), false)));
        Assert.Empty(ship.NavigationRoute);
    }

    [Fact]
    public void InvalidRouteCannotReplaceAcceptedPlan()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Navigator");
        var point = new NavigationPoint((uint)world.Catalog.Station.Id);
        Assert.True(world.SetRoute(ship.Id,new(1,new[]{point},false)));
        foreach (var bad in new[] { new NavigationPoint(ship.Id), new NavigationPoint(999999),
            new NavigationPoint(0,new Vec3d(double.NaN,0,0)),new NavigationPoint(point.TargetId,Vec3d.Zero) })
            Assert.False(world.SetRoute(ship.Id,new(2,new[]{bad},true)));
        Assert.False(world.SetRoute(ship.Id,new(2,Enumerable.Repeat(point,25).ToArray(),true)));
        Assert.Equal(1u,ship.RouteRevision);
        Assert.Equal(point,Assert.Single(ship.NavigationRoute));
    }

    [Fact]
    public void NearbyWaypointsAreVisitedInOrderAndStop()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Navigator");
        ship.Position=new Vec3d(-30_000,20_000,0);
        var points = new[] { new NavigationPoint(0,ship.Position+new Vec3d(0,300,-600)),new NavigationPoint(0,ship.Position+new Vec3d(-500,300,-600)) };
        Assert.True(world.SetRoute(ship.Id,new(1,points,true)));
        var visited=false;
        for(var i=0;i<6000&&ship.RouteActive;i++)
        {
            world.ApplyInput(ship.Id,InputState.None);world.Tick(.05);
            if(ship.RouteIndex==1)visited=true;
        }
        Assert.True(visited);
        Assert.False(ship.RouteActive);
        Assert.Equal(2,ship.RouteIndex);
        Assert.True(ApproachPilot.Arrived(ship,points[1].Position!.Value),$"Distance {Vec3d.Distance(ship.Position,points[1].Position!.Value)}, speed {ship.Velocity.Length}");
    }

    [Fact]
    public void DistantRouteLeavesStationBoostsCruisesAndWarps()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Navigator");
        var boost=false;var cruise=false;var warp=false;var edited=false;
        Assert.True(world.SetRoute(ship.Id,new(1,new[]{new NavigationPoint((uint)world.Catalog.Star.Id)},true)));
        for(var i=0;i<10000&&ship.RouteActive;i++)
        {
            world.ApplyInput(ship.Id,InputState.None); world.Tick(.05);
            boost|=ship.Boosting;cruise|=ship.Cruising;warp|=ship.InWarp;
            if(ship.InWarp&&!edited)
            {
                var endpoint=ship.WarpDestination;
                Assert.True(world.SetRoute(ship.Id,new(2,new[]{new NavigationPoint((uint)world.Catalog.Star.Id)},true)));
                Assert.Equal(endpoint,ship.WarpDestination);
                Assert.True(ship.InWarp);edited=true;
            }
        }
        Assert.True(boost,"Departure must use boost.");
        Assert.True(cruise,"Departure must reach cruise.");
        Assert.True(warp,$"Must warp: active {ship.RouteActive}, position {ship.Position}, speed {ship.Velocity.Length}");
        Assert.False(ship.RouteActive);Assert.Equal(1,ship.RouteIndex);
    }

    [Fact]
    public void RouteWireRoundTripAndBounds()
    {
        var route=new RouteRequestMessage(42,new[]{new NavigationPoint(1),new NavigationPoint(0,new Vec3d(1,2,3))},true);
        Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.Encode(route),out var decoded,out _));
        var copy=Assert.IsType<RouteRequestMessage>(decoded);
        Assert.Equal(route.RequestId,copy.RequestId);Assert.Equal(route.Points,copy.Points);Assert.True(copy.Enabled);
        Assert.Throws<ArgumentException>(()=>ProtocolCodec.Encode(route with {Points=Enumerable.Repeat(new NavigationPoint(1),25).ToArray()}));
        var packet=ProtocolCodec.Encode(route);packet[9]=25;
        Assert.False(ProtocolCodec.TryDecode(packet,out _,out _));
        var snapshot=new SnapshotMessage(1,1,new[]{new EntitySnapshot(10000,EntityKind.Player,"Pilot",Vec3d.Zero,0,0,Vec3d.Zero,0,RouteRevision:42,RouteIndex:2,RouteActive:true,Boosting:true)});
        Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.Encode(snapshot),out decoded,out _));
        Assert.Equal(snapshot.Entities,Assert.IsType<SnapshotMessage>(decoded).Entities);
    }
}
