using System;
using System.Collections.Generic;

namespace IndifferentStars.Shared.World;

public sealed partial class WorldCatalog
{
    private static (CelestialBody Star, CelestialBody Primary, IReadOnlyList<CelestialBody> Additional) GenerateCelestials(int seed,int system,string name)
    {
        var stars=new SeedStream(seed,system,"stellar-physics");
        var kind=(StellarClass)stars.Range(0,5);
        var temperature=kind switch {StellarClass.RedDwarf=>stars.Between(2900,3900),StellarClass.OrangeDwarf=>stars.Between(4000,5100),
            StellarClass.YellowDwarf=>stars.Between(5300,6200),StellarClass.WhiteStar=>stars.Between(7200,9800),_=>stars.Between(11000,18000)};
        var size=kind switch {StellarClass.RedDwarf=>stars.Between(.25,.6),StellarClass.OrangeDwarf=>stars.Between(.65,.9),
            StellarClass.YellowDwarf=>stars.Between(.9,1.2),StellarClass.WhiteStar=>stars.Between(1.3,2.2),_=>stars.Between(2.3,4.5)};
        var star=new CelestialBody {Id=1,Kind=CelestialKind.Star,Name=name,SystemPosition=Vec3d.Zero,Radius=696_000_000*size,
            StellarClass=kind,TemperatureKelvin=temperature,ParentId=0};
        var layout=new SeedStream(seed,system,"planet-layout");
        var count=layout.Range(5,10);
        var primaryIndex=layout.Range(3,count-1);
        var bodies=new List<CelestialBody>();CelestialBody primary=null!;
        var orbitalRadii=new double[count];var cursor=layout.Between(.22,.38);
        for(var i=0;i<count;i++){orbitalRadii[i]=cursor;cursor*=layout.Between(1.45,1.85);}
        var habitableScale=Math.Sqrt(size*size*Math.Pow(temperature/5778,4))/orbitalRadii[primaryIndex-1];
        var names=new SeedStream(seed,system,"planet-names");
        for(var i=0;i<count;i++)
        {
            var detail=new SeedStream(seed,system,$"planet-{i}");
            var angle=layout.Between(0,Math.Tau);
            var surface=i==primaryIndex?PlanetSurface.Gas : i==0?PlanetSurface.Scorched : i==1?PlanetSurface.Cratered :
                i==primaryIndex-1?PlanetSurface.Terrestrial : i==count-1?PlanetSurface.Ice : (PlanetSurface)detail.Range(0,5);
            var gas=surface==PlanetSurface.Gas;
            var radius=gas?detail.Between(7_450_000,48_000_000):detail.Between(2_800_000,8_400_000);
            var body=new CelestialBody {Id=i==primaryIndex?2:10+i,Kind=CelestialKind.Planet,Name=$"{names.Name()} {system+1}-{i+1}",
                SystemPosition=new Vec3d(Math.Cos(angle),layout.Between(-.025,.025),Math.Sin(angle))*orbitalRadii[i]*habitableScale*NavigationLimits.AstronomicalUnit,
                Radius=radius,Surface=surface,SurfaceSeed=detail.Range(0,1000000),AxialTilt=(float)detail.Between(.02,.9),
                AtmosphereHeight=surface==PlanetSurface.Terrestrial?100_000:gas?140_000:surface==PlanetSurface.Scorched?20_000:0,
                RingInnerRadii=i==primaryIndex?1.55:0,RingOuterRadii=i==primaryIndex?2.35:0};
            if(i==primaryIndex)primary=body;else bodies.Add(body);
            var moons=gas?detail.Range(1,4):detail.Range(0,2);
            for(var m=0;m<moons;m++)
            {
                var moonRadius=detail.Between(300_000,Math.Min(1_900_000,radius*.3));
                var phase=detail.Between(0,Math.Tau);var orbit=radius*(4+m*2.5);
                bodies.Add(new CelestialBody {Id=100+i*8+m,Kind=CelestialKind.Moon,ParentId=body.Id,Name=$"{body.Name} — {m+1}",
                    Radius=moonRadius,SystemPosition=body.SystemPosition+new Vec3d(Math.Cos(phase)*orbit,orbit*.12,Math.Sin(phase)*orbit),
                    Surface=surface==PlanetSurface.Ice?PlanetSurface.Ice:PlanetSurface.Cratered,SurfaceSeed=detail.Range(0,1000000),AxialTilt=.1f});
            }
        }
        return(star,primary,bodies.AsReadOnly());
    }
}
