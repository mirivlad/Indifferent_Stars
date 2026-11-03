using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;
namespace IndifferentStars.Tests;
public sealed class RegionalSystemTests
{
    [Fact]
    public void ResourcesAndStationsAreDistributedAndBodiesHaveUniqueIds()
    {
        var a=WorldCatalog.Generate(20261101);var b=WorldCatalog.Generate(20261102);
        Assert.InRange(a.Stations.Count,4,6);Assert.Equal(2,a.Bands.Count);
        Assert.Contains(a.Stations,s=>s.Purpose==StationPurpose.Mining);
        Assert.Contains(a.Stations,s=>s.Purpose==StationPurpose.Research);
        Assert.Contains(a.Stations,s=>s.Purpose==StationPurpose.Freight);
        Assert.Equal(a.LocalBodies.Count,a.LocalBodies.Select(x=>x.Id).Distinct().Count());
        Assert.All(a.AdditionalStations,s=>Assert.True(s.Position.Length>1_000_000_000));
        Assert.True(a.Asteroids.Count>500);
        Assert.NotEqual(a.Sectors.Where(s=>s.Id.StartsWith("resource-")).Select(s=>s.Center),b.Sectors.Where(s=>s.Id.StartsWith("resource-")).Select(s=>s.Center));
        foreach(var station in a.Stations)Assert.Same(station,a.Find(station.Id));
    }
    [Fact]
    public void SeededStationModulesHaveExactCollisionSurfacesAndDifferentLayouts()
    {
        var a=WorldCatalog.Generate(20261101);var b=WorldCatalog.Generate(20261102);
        Assert.NotEqual(a.Station.Assembly!.Signature,b.Station.Assembly!.Signature);
        foreach(var station in a.Stations)
        {
            var assembly=station.Assembly!;
            Assert.NotEmpty(assembly.Modules);
            Assert.Equal(assembly.Signature,StationAssembly.Generate(station.DesignSeed,station.Purpose).Signature);
            foreach(var module in assembly.Modules)
            foreach(var t in module.Triangles().Where((_,i)=>i%13==0))
            {
                Assert.True(Vec3d.Cross(t.B-t.A,t.C-t.A).Length>1e-4);
                var center=(t.A+t.B+t.C)/(3*ModelPose.StationScale);
                Assert.InRange(SurfaceQuery.Distance(assembly.Surface,center),0,.00001);
            }
        }
    }

    [Fact]
    public void DistantStationDockingAndMarketsAreIndependent()
    {
        var c=WorldCatalog.Generate(20261101);var w=new GameWorld(c);var a=w.AddPlayer("A");var b=w.AddPlayer("B");
        var first=c.Station;var second=c.AdditionalStations.Last();
        a.Position=first.Position+ModelPose.StationDockingOffset;b.Position=second.Position+ModelPose.StationDockingOffset;
        Assert.True(w.RequestDocking(a.Id,(uint)first.Id));Assert.True(w.RequestDocking(b.Id,(uint)second.Id));
        w.Tick(.05);Assert.True(a.Docked&&b.Docked);
        Assert.Equal((uint)second.Id,b.DockingStationId);
        var original=w.GetMarket(b.Id)!.Entries.First(e=>e.CommodityId==1).StationStock;
        Assert.True(w.Trade(a.Id,1,1,true).Success);
        Assert.Equal(original,w.GetMarket(b.Id)!.Entries.First(e=>e.CommodityId==1).StationStock);
        Assert.True(w.Undock(b.Id));Assert.False(b.Docked);
    }
    [Fact]
    public void ContinuousCollisionStillHitsDistantStationAndAsteroidShells()
    {
        foreach(var asteroid in new[]{false,true})
        {
            var c=WorldCatalog.Generate(20261101);var w=new GameWorld(c);var ship=w.AddPlayer("Collision");
            var body=asteroid?c.Sectors.First(s=>s.Id=="resource-1").Bodies.First():c.AdditionalStations.Last();
            var surface=WorldSurfaces.Body(c,body,0);var direction=new Vec3d(1,.35,.2).Normalized();
            ship.Position=body.Position+direction*(surface.Radius+100);ship.Velocity=-direction*100_000;
            w.Tick(.05);
            Assert.NotNull(ship.Impact);Assert.True(surface.Distance(ship.Position)>0);
            Assert.True(Vec3d.Distance(ship.Position,body.Position)<surface.Radius+200);
        }
    }
    [Fact]
    public void DistantStationCustomWarpAndDockSnapshotRoundTrip()
    {
        var c=WorldCatalog.Generate(20261101);var w=new GameWorld(c);var ship=w.AddPlayer("Warp");ship.Position=new Vec3d(0,100000,0);
        foreach(var station in c.AdditionalStations)
        {
            ship.WarpStage=WarpStage.None;ship.Position=new Vec3d(0,100000,0);
            var result=w.Warp(ship.Id,(uint)station.Id,surfaceDistance:500);Assert.True(result.Success,result.Message);
            var pose=WorldSurfaces.Body(c,station,WarpFlight.ChargeSeconds+WarpFlight.TravelSeconds);
            Assert.InRange(pose.Distance(result.Destination),499.95,500.05);
        }
        ship.DockingStationId=6;
        var packet=new SnapshotMessage(1,1,w.BuildSnapshot(ship.Id));
        Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.EncodeSnapshotPackets(packet)[0],out var decoded,out _));
        Assert.Equal(6u,Assert.IsType<SnapshotMessage>(decoded).Entities.Single(e=>e.Id==ship.Id).DockingStationId);
    }
}
