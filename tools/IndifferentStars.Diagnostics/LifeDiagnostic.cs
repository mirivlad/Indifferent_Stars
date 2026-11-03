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
        var world=new GameWorld(WorldCatalog.Generate(20261101));var times=new List<double>();
        for(var i=0;i<ticks;i++)
        {
            var start=Stopwatch.GetTimestamp();world.Tick(.05);times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if(i%200==199)Console.WriteLine(JsonSerializer.Serialize(new {time=world.TimeSeconds,ships=world.Npcs.Select(n=>new {n.Id,job=n.Work!.Occupation.ToString(),phase=n.Work.Phase.ToString(),n.Work.Cargo,n.Work.Deliveries,range=Vec3d.Distance(n.Position,n.Work.Goal),speed=n.Velocity.Length,route=n.RouteActive,warp=n.WarpStage.ToString()})}));
        }
        times.Sort();Console.WriteLine(JsonSerializer.Serialize(new {ticks,npcs=world.Npcs.Count,bodies=world.Catalog.LocalBodies.Count,p50=times[times.Count/2],p95=times[(int)(times.Count*.95)],max=times[^1],events=world.Activities.Count}));
    }
}
