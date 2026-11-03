using System;
using System.Collections.Generic;
using System.Linq;

namespace IndifferentStars.Shared.World;

public enum LinkKind : byte { Gate, Wormhole }
public sealed record SystemLink(int First, int Second, LinkKind Kind);
public sealed record SystemAddress(int Index, string Name, Vec3d PositionLightYears);

/// <summary>Seeded topology only. Ships do not cross these links until intersystem travel is implemented.</summary>
public sealed class GalaxyLayout
{
    public const int GeneratorVersion = 3;
    public required IReadOnlyList<SystemAddress> Systems { get; init; }
    public required IReadOnlyList<SystemLink> Links { get; init; }
    public static GalaxyLayout Generate(int seed)
    {
        var count = new SeedStream(seed,0,"galaxy-count").Range(48,97);
        var systems = new List<SystemAddress>();
        for(var i=0;i<count;i++)
        {
            var names=new SeedStream(seed,i,"system-name");
            var place=new SeedStream(seed,i,"galactic-position");
            systems.Add(new(i,$"{names.Name()} {i+1}",new Vec3d(place.Between(-80,80),place.Between(-8,8),place.Between(-80,80))));
        }
        var links=new List<SystemLink>();
        // A connected nearest-growth backbone, then sparse extra connections.
        for(var i=1;i<count;i++)
        {
            var nearest=Enumerable.Range(0,i).MinBy(j=>Vec3d.Distance(systems[i].PositionLightYears,systems[j].PositionLightYears));
            links.Add(new(nearest,i,LinkKind.Gate));
        }
        var random=new SeedStream(seed,0,"galactic-links");
        for(var i=0;i<count/3;i++)
        {
            var a=random.Range(0,count);var b=random.Range(0,count);
            if(a==b)continue;
            var first=Math.Min(a,b);var second=Math.Max(a,b);
            if(links.Any(l=>l.First==first&&l.Second==second))continue;
            links.Add(new(first,second,i%4==0?LinkKind.Wormhole:LinkKind.Gate));
        }
        return new GalaxyLayout {Systems=systems.AsReadOnly(),Links=links.AsReadOnly()};
    }
}
