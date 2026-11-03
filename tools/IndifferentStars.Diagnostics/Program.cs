using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

const int samples = 64;
const double step = .05;
var records = new List<object>();
var cases = new[]
{
    ("far", new Vec3d(0, 8, 0)),
    ("open_ring", new Vec3d(2, 0, 0)),
    ("rim_hover", new Vec3d(2.9, .36, 0)),
    ("rim_close", new Vec3d(2.9, .23 + 7 / ModelPose.StationScale, 0)),
    ("rim_contact", new Vec3d(2.9, .24, 0)),
    ("hub_near", new Vec3d(1.1, 0, 0)),
    ("dock_hover", ModelPose.StationDockingOffset / ModelPose.StationScale),
};
foreach(var (name, local) in cases)
{
    var point = local * ModelPose.StationScale;
    Measure("surface_sweep", name, () =>
    {
        _ = SurfaceContact.Sweep(SurfaceMesh.Ship,1,point,point+new Vec3d(0,-.25,0),Quaternion.Identity,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY,.01f),
            SurfaceMesh.Station,ModelPose.StationScale,Vec3d.Zero,Vec3d.Zero,Quaternion.Identity,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(ModelPose.StationSpin*step)));
    });
    var catalog = WorldCatalog.Generate(20261101);
    var world = new GameWorld(catalog);
    var ship = world.AddPlayer("Diagnostic");
    Measure("world_tick_8_npc",name,()=>world.Tick(step),()=>
    {
        var rotation = ModelPose.StationRotation(catalog.Station,world.TimeSeconds);
        ship.Position = SurfaceMesh.Rotate(point,rotation);
        ship.Orientation = rotation;
        ship.Velocity = Vec3d.Zero;
        world.ApplyInput(ship.Id,InputState.None);
    });
}
Measure("surface_sweep", "opposing_ships", () =>
    _ = SurfaceContact.Sweep(SurfaceMesh.Ship, 1, new(-100,0,0), new(100,0,0),
        Quaternion.Identity, Quaternion.Identity, SurfaceMesh.Ship, 1,
        new(100,0,0), new(-100,0,0), Quaternion.Identity, Quaternion.Identity));
Measure("surface_sweep", "rotating_ship", () =>
    _ = SurfaceContact.Sweep(SurfaceMesh.Ship, 1, Vec3d.Zero, Vec3d.Zero,
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.275f),
        SurfaceMesh.Ship, 1, new(0,5.6,0), new(0,5.6,0), Quaternion.Identity, Quaternion.Identity));
var result = new
{
    seed=20261101, samples, tick_seconds=step, station_scale=ModelPose.StationScale,
    station_triangles=SurfaceMesh.Station.Triangles.Length, ship_triangles=SurfaceMesh.Ship.Triangles.Length,
    geometry=SurfaceMesh.Fingerprint,
    runtime=Environment.Version.ToString(), os=Environment.OSVersion.ToString(),
    stopwatch_frequency=Stopwatch.Frequency, processor_count=Environment.ProcessorCount,
    tiered_compilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
#if DEBUG
    configuration="Debug",
#else
    configuration="Release",
#endif
    metrics=records,
};
if(args.Length>0) File.WriteAllText(args[0],JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));

void Measure(string metric,string scenario,Action run,Action? prepare=null)
{
    for(var i=0;i<8;i++) {prepare?.Invoke();run();}
    var times = new double[samples];
    long bytes=0;
    for(var i=0;i<samples;i++)
    {
        prepare?.Invoke();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        run();
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        bytes += GC.GetAllocatedBytesForCurrentThread()-allocated;
    }
    Array.Sort(times);
    var entry = new {metric,scenario,p50_ms=times[samples/2],p95_ms=times[(int)(samples*.95)],
        max_ms=times[^1],allocated_bytes_per_call=bytes/samples};
    records.Add(entry);
    Console.WriteLine(JsonSerializer.Serialize(entry));
}
