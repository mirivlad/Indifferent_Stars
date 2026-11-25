using System.Diagnostics;
using System.Security.Cryptography;
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
        var times = new double[ticks];
        var near=new List<int>();var departures=0;var arrivals=0;var extraction=0;
        var slowTicks = new List<SlowTick>();
        long allocated = 0;
        var initialCollections = Enumerable.Range(0, GC.MaxGeneration + 1).Select(GC.CollectionCount).ToArray();
        for(var i=0;i<ticks;i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var collections = GC.CollectionCount(0);
            var staticBefore = world.StaticCollisionMilliseconds;
            var dynamicBefore = world.DynamicCollisionMilliseconds;
            var start = Stopwatch.GetTimestamp();
            world.Tick(.05);
            var elapsed = times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated += bytes;
            if (elapsed > 50)
            {
                var sweep = world.SlowestShipSweep;
                slowTicks.Add(new(i + 1, elapsed, world.StaticCollisionMilliseconds - staticBefore,
                    world.DynamicCollisionMilliseconds - dynamicBefore, bytes, GC.CollectionCount(0) != collections,
                    new(sweep.A, sweep.B, sweep.Ms, sweep.Separation, sweep.RelativeSpeed,
                        Describe(sweep.A), Describe(sweep.B))));
            }
            near.Add(world.Npcs.Count(n=>!n.InWarp&&Vec3d.Distance(n.Position,world.Catalog.Station.Position)<150000));
            if (i%2000==1999) Console.Error.WriteLine($"life progress: tick={i+1}, time={world.TimeSeconds:F0}s, deliveries={world.Npcs.Sum(n=>n.Work!.Deliveries)}");
            while(world.TryTakeActivity(out var activity))
            {if(activity.Kind=="departure")departures++;if(activity.Kind=="arrival")arrivals++;if(activity.Kind=="extraction")extraction++;}
        }
        var state = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(world.BuildSnapshot())));
        Array.Sort(times);
        Console.WriteLine(JsonSerializer.Serialize(new {seed,system,ticks,npcs=world.Npcs.Count,
            bodies=world.Catalog.LocalBodies.Count,activity=world.Catalog.ActivityLevel,
            mean=times.Average(),p50=times[ticks/2],p95=times[(int)(ticks*.95)],p99=times[(int)(ticks*.99)],max=times[^1],
            over_50_ms=slowTicks.Count, over_budget_with_gc=slowTicks.Count(t=>t.Gc),
            tick_avg_allocated_bytes=allocated / ticks,
            collections=initialCollections.Select((count,generation)=>GC.CollectionCount(generation)-count),
            slowest=slowTicks.OrderByDescending(t=>t.Ms).Take(5),
            state,
            route_ms_per_tick=world.RouteMilliseconds/ticks,safety_ms_per_tick=world.CruiseSafetyMilliseconds/ticks,
            collision_ms_per_tick=world.CollisionMilliseconds/ticks,
            static_ms_per_tick=world.StaticCollisionMilliseconds/ticks,dynamic_ms_per_tick=world.DynamicCollisionMilliseconds/ticks,near_mean=near.Average(),near_max=near.Max(),
            departures,arrivals,extraction,deliveries=world.Npcs.Sum(n=>n.Work!.Deliveries),
            body_hotspots=world.CollisionBodyHotspots.OrderByDescending(p=>p.Value).Take(4).Select(p=>new {p.Key,ms=p.Value/ticks,kind=world.Catalog.Find(p.Key)?.Kind.ToString()}),
            hotspots=world.CollisionHotspots.OrderByDescending(p=>p.Value).Take(6).Select(p=>new {p.Key,ms=p.Value/ticks,ship=world.Npcs.Where(n=>n.Id==p.Key).Select(n=>new {pos=n.Position.ToString(),goal=n.Work!.Goal,speed=n.Velocity.Length,phase=n.Work.Phase.ToString(),hit=n.ImpactSequence,home=n.Work.HomeStationId})}),
            phases=world.Npcs.GroupBy(n=>n.Work!.Phase.ToString()).ToDictionary(g=>g.Key,g=>g.Count())}));

        string? Describe(uint id) => world.Npcs.FirstOrDefault(ship => ship.Id == id) is { Work: { } work } ship
            ? $"{work.Phase} #{work.TargetId}, goal {work.Goal}, position {ship.Position}" : null;
    }

    private sealed record SlowTick(int Tick, double Ms, double StaticMs, double DynamicMs, long Allocated, bool Gc, ShipSweep Sweep);
    private sealed record ShipSweep(uint A, uint B, double Ms, double Separation, double RelativeSpeed, string? StateA, string? StateB);
}
