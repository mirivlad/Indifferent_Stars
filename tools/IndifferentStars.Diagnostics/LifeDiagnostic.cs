using System.Diagnostics;
using System.Text.Json;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
public static class LifeDiagnostic
{
    public static void Run(string[] args)
    {
        var ticks=args.Length>1?Math.Clamp(int.Parse(args[1]),1,20000):1000;
        var seed=args.Length>2?int.Parse(args[2]):20261101;
        var system=args.Length>3?int.Parse(args[3]):0;
        var world=new GameWorld(WorldCatalog.Generate(seed,system)) {MeasureTimings=true};
        var times=new List<double>();var near=new List<int>();var departures=0;var arrivals=0;var extraction=0;
        for(var i=0;i<ticks;i++)
        {
            var start=Stopwatch.GetTimestamp();world.Tick(.05);times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            near.Add(world.Npcs.Count(n=>!n.InWarp&&Vec3d.Distance(n.Position,world.Catalog.Station.Position)<150000));
            if (i%2000==1999) Console.Error.WriteLine($"life progress: tick={i+1}, time={world.TimeSeconds:F0}s, deliveries={world.Npcs.Sum(n=>n.Work!.Deliveries)}");
            while(world.TryTakeActivity(out var activity))
            {if(activity.Kind=="departure")departures++;if(activity.Kind=="arrival")arrivals++;if(activity.Kind=="extraction")extraction++;}
        }
        times.Sort();Console.WriteLine(JsonSerializer.Serialize(new {seed,system,ticks,npcs=world.Npcs.Count,
            bodies=world.Catalog.LocalBodies.Count,activity=world.Catalog.ActivityLevel,
            mean=times.Average(),p50=times[times.Count/2],p95=times[(int)(times.Count*.95)],max=times[^1],
            route_ms_per_tick=world.RouteMilliseconds/ticks,safety_ms_per_tick=world.CruiseSafetyMilliseconds/ticks,
            collision_ms_per_tick=world.CollisionMilliseconds/ticks,
            static_ms_per_tick=world.StaticCollisionMilliseconds/ticks,dynamic_ms_per_tick=world.DynamicCollisionMilliseconds/ticks,near_mean=near.Average(),near_max=near.Max(),
            departures,arrivals,extraction,deliveries=world.Npcs.Sum(n=>n.Work!.Deliveries),
            body_hotspots=world.CollisionBodyHotspots.OrderByDescending(p=>p.Value).Take(4).Select(p=>new {p.Key,ms=p.Value/ticks,kind=world.Catalog.Find(p.Key)?.Kind.ToString()}),
            hotspots=world.CollisionHotspots.OrderByDescending(p=>p.Value).Take(6).Select(p=>new {p.Key,ms=p.Value/ticks,ship=world.Npcs.Where(n=>n.Id==p.Key).Select(n=>new {pos=n.Position.ToString(),goal=n.Work!.Goal,speed=n.Velocity.Length,phase=n.Work.Phase.ToString(),hit=n.ImpactSequence,home=n.Work.HomeStationId})}),
            phases=world.Npcs.GroupBy(n=>n.Work!.Phase.ToString()).ToDictionary(g=>g.Key,g=>g.Count())}));
    }
}
