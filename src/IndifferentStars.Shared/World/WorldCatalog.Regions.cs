using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Shared.World;

public sealed record AsteroidBand(string Name, double Radius, double Width, bool Icy);

public sealed partial class WorldCatalog
{
    private static (IReadOnlyList<StaticBody> Stations,IReadOnlyList<SectorDefinition> Sectors,IReadOnlyList<AsteroidBand> Bands)
        GenerateRegions(int seed,int system,CelestialBody star,CelestialBody primary,IReadOnlyList<CelestialBody> additional,Vec3d origin)
    {
        var config=new SeedStream(seed,system,"regional-layout");
        var planets=additional.Where(b=>b.Kind==CelestialKind.Planet).Append(primary).OrderBy(b=>b.SystemPosition.Length).ToArray();
        var inner=(planets[1].SystemPosition.Length+planets[2].SystemPosition.Length)*.5;
        var outer=planets[^1].SystemPosition.Length*1.2;
        var bands=new[]{new AsteroidBand(star.Name+" Main Belt",inner,inner*.06,false),new AsteroidBand(star.Name+" Ice Reach",outer,outer*.09,true)};
        var sectors=new List<SectorDefinition>();var stations=new List<StaticBody>();
        var pockets=config.Range(6,11);
        for(var i=0;i<pockets;i++)
        {
            var random=new SeedStream(seed,system,$"asteroid-region-{i}");
            var band=bands[i%2];var angle=Math.Tau*i/pockets+random.Between(-.1,.1);
            var center=new Vec3d(Math.Cos(angle),random.Between(-.008,.008),Math.Sin(angle))*band.Radius-origin;
            var bodies=new List<StaticBody>();var count=random.Range(35,81);
            for(var j=0;j<count;j++)
            {
                var phase=random.Between(0,Math.Tau);var radius=random.Between(1500,10000);
                bodies.Add(new StaticBody {Id=3000+i*100+j,Kind=BodyKind.Asteroid,Name=$"{band.Name} {i+1}-{j+1}",
                    Position=center+new Vec3d(Math.Cos(phase)*radius,random.Between(-1400,1400),Math.Sin(phase)*radius),
                    Icy=band.Icy,Radius=random.Between(12,110),Rotation=(float)random.Between(0,Math.Tau),Variant=random.Range(0,3)});
            }
            if(i==0)
            {
                var station=CreateStation(4,StationPurpose.Mining,band.Name+" Extraction",center+new Vec3d(0,8000,0),random);
                stations.Add(station);bodies.Add(station);
            }
            sectors.Add(new SectorDefinition {Id=$"resource-{i}",Name=band.Name+$" / {i+1}",Kind=SectorKind.Asteroid,Center=center,Radius=12500,Bodies=bodies.AsReadOnly()});
        }
        var earth=planets.First(b=>b.Surface==PlanetSurface.Terrestrial);
        var ice=planets.Last(b=>b.Surface==PlanetSurface.Ice);
        foreach(var (id,purpose,body,label) in new[]{(5,StationPurpose.Research,ice,"Observatory"),(6,StationPurpose.Freight,earth,"Transit")})
        {
            var random=new SeedStream(seed,system,$"station-{id}");
            var station=CreateStation(id,purpose,body.Name+" "+label,body.SystemPosition-origin+Vec3d.Up*(body.Radius+900000),random);
            stations.Add(station);
            sectors.Add(new SectorDefinition {Id=$"station-{id}",Name=station.Name,Kind=SectorKind.Habitated,Center=station.Position,Radius=9000,Bodies=new[]{station}});
        }
        var extras=new SeedStream(seed,system,"optional-outposts");
        var extraCount=extras.Range(0,3);
        for(var i=0;i<extraCount;i++)
        {
            var body=planets[extras.Range(0,planets.Length)];var angle=extras.Between(0,Math.Tau);
            var radial=new Vec3d(Math.Cos(angle),.4,Math.Sin(angle)).Normalized();
            var purpose=(StationPurpose)extras.Range(0,4);
            var station=CreateStation(7+i,purpose,body.Name+$" Outpost {i+1}",body.SystemPosition-origin+radial*(body.Radius+extras.Between(900000,2500000)),extras);
            stations.Add(station);sectors.Add(new SectorDefinition {Id=$"station-{station.Id}",Name=station.Name,Kind=SectorKind.Habitated,Center=station.Position,Radius=9000,Bodies=new[]{station}});
        }
        return(stations.AsReadOnly(),sectors.AsReadOnly(),bands);
    }
    private static StaticBody CreateStation(int id,StationPurpose purpose,string name,Vec3d position,SeedStream random)
    {
        var designSeed=random.Range(1,int.MaxValue);
        return new StaticBody {Id=id,Kind=BodyKind.Station,Name=name,Position=position,DesignSeed=designSeed,
            Radius=StationAssembly.Generate(designSeed,purpose).RadiusMetres,
            Rotation=(float)random.Between(0,Math.Tau),Purpose=purpose};
    }
}
