using System.Text.Json;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class SeededSystemTests
{
    [Fact]
    public void SeedAndSystemReproduceEntireCatalogAndDifferentSeedsDiffer()
    {
        var a=WorldCatalog.Generate(20261101);var b=WorldCatalog.Generate(20261101);
        Assert.Equal(JsonSerializer.Serialize(a.Celestials),JsonSerializer.Serialize(b.Celestials));
        Assert.Equal(JsonSerializer.Serialize(a.Asteroids),JsonSerializer.Serialize(b.Asteroids));
        Assert.NotEqual(JsonSerializer.Serialize(a.Celestials),JsonSerializer.Serialize(WorldCatalog.Generate(20261102).Celestials));
        Assert.NotEqual(a.Star.Name,WorldCatalog.Generate(20261101,1).Star.Name);
        var random=new SeedStream(1,0,"asteroids");for(var i=0;i<100;i++)random.Next();
        Assert.Equal(new SeedStream(1,0,"names").Next(),new SeedStream(1,0,"names").Next());
    }
    [Fact]
    public void GeneratedSystemsHaveDistinctPlanetsMoonsAndNoIntersections()
    {
        var counts=new HashSet<int>();var stars=new HashSet<StellarClass>();
        for(var seed=0;seed<32;seed++)
        {
            var c=WorldCatalog.Generate(seed);var planets=c.Celestials.Where(b=>b.Kind==CelestialKind.Planet).ToArray();
            counts.Add(planets.Length);stars.Add(c.Star.StellarClass);
            Assert.InRange(planets.Length,5,9);
            Assert.Contains(planets,b=>b.Surface==PlanetSurface.Terrestrial);
            Assert.Contains(planets,b=>b.Surface==PlanetSurface.Ice);
            Assert.Contains(c.Celestials,b=>b.Kind==CelestialKind.Moon);
            Assert.Equal(c.Celestials.Count,c.Celestials.Select(b=>b.Id).Distinct().Count());
            Assert.Equal(c.Celestials.Count,c.Celestials.Select(b=>b.Name).Distinct().Count());
            foreach(var body in c.Celestials)
            {
                Assert.Same(body,c.FindCelestial(body.Id));Assert.True(body.SystemPosition.IsFinite&&body.Radius>0);
                if(body.Kind==CelestialKind.Star)continue;
                var parent=c.FindCelestial(body.ParentId)!;
                Assert.True(Vec3d.Distance(body.SystemPosition,parent.SystemPosition)>parent.Radius+body.Radius);
                var point=body.SystemPosition+Vec3d.Up*(body.Radius+500000);
                Assert.Same(body,SurfaceNavigation.Nearest(c,point).Body);
            }
        }
        Assert.True(counts.Count>1);Assert.Equal(5,stars.Count);
    }
    [Fact]
    public void GalaxyTopologyIsConnectedUniqueAndDeterministic()
    {
        var a=GalaxyLayout.Generate(42);var b=GalaxyLayout.Generate(42);
        Assert.Equal(JsonSerializer.Serialize(a),JsonSerializer.Serialize(b));
        Assert.NotEqual(JsonSerializer.Serialize(a),JsonSerializer.Serialize(GalaxyLayout.Generate(43)));
        Assert.Equal(a.Systems.Count,a.Systems.Select(s=>s.Name).Distinct().Count());
        Assert.Contains(a.Links,l=>l.Kind==LinkKind.Wormhole);
        var reached=new HashSet<int>{0};
        for(var i=0;i<a.Systems.Count;i++)foreach(var link in a.Links)
        {if(reached.Contains(link.First))reached.Add(link.Second);if(reached.Contains(link.Second))reached.Add(link.First);}
        Assert.Equal(a.Systems.Count,reached.Count);
        Assert.All(a.Links,l=>Assert.True(l.First<l.Second));
        Assert.Equal(a.Links.Count,a.Links.Select(l=>(l.First,l.Second)).Distinct().Count());
    }
    [Fact]
    public void AllCelestialsParticipateInWarpOrbitAndCollisionSafety()
    {
        var c=WorldCatalog.Generate(20261101);var world=new GameWorld(c);var ship=world.AddPlayer("Explorer");
        foreach(var body in c.AdditionalCelestials)
        {
            ship.Position=new Vec3d(0,100000,0);ship.WarpStage=WarpStage.None;
            var warp=world.Warp(ship.Id,(uint)body.Id);Assert.True(warp.Success,$"{body.Name}: {warp.Message}");
            Assert.True(Vec3d.Distance(warp.Destination,c.CelestialOffset(body))>body.Radius);
            ship.WarpStage=WarpStage.None;
            Assert.True(world.StartOrbit(ship.Id,new(1,(uint)body.Id,500000)).Accepted);
            var unsafePoint=c.CelestialOffset(body);
            Assert.False(world.Warp(ship.Id,0,unsafePoint).Success);
        }
    }
}
