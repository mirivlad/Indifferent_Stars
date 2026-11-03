using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

public sealed partial class SeededSystemChecks : Node3D
{
    public override async void _Ready()
    {
        try
        {
            Locale.Load("ru");
            var output=System.Environment.GetEnvironmentVariable("IS_SYSTEM_CAPTURE")??"/tmp/indifferent-system";
            Directory.CreateDirectory(output);
            var catalog=WorldCatalog.Generate(20261101);
            var camera=new Camera3D {Current=true,Fov=65,Near=.35f,Far=400000};AddChild(camera);
            var world=new Node3D();AddChild(world);var builder=new WorldBuilder(world,catalog,true);builder.Build();
            foreach(var body in catalog.Celestials.Where(b=>b.Kind==CelestialKind.Planet))
            {
                var radial=((catalog.Star.SystemPosition-body.SystemPosition).Normalized()+new Vec3d(.2,.3,.1)).Normalized();
                var observer=catalog.CelestialOffset(body)+radial*(body.Radius*3);
                var origin=RenderFrame.ChooseOrigin(observer,Vec3d.Zero);
                world.Position=-ShipTrack.ToVector3(origin);camera.Position=ShipTrack.ToVector3(observer-origin);
                builder.Process(0,observer,origin);camera.Far=builder.RequiredFarPlane;camera.Near=Math.Max(.35f,camera.Far/8000000);
                var holder=body.Id==catalog.Planet.Id?world.GetNode<Node3D>("CelestialBodies/PlanetAsterReach"):world.GetNode<Node3D>($"CelestialBodies/Celestial_{body.Id}");
                camera.LookAt(holder.GlobalPosition,Vector3.Up);
                await Pause(.2);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,$"planet_{body.Id}_{body.Surface}.png"));
                GD.Print($"BODY {body.Id}: {body.Name}, {body.Surface}, radius {body.Radius/1000:F0} km");
            }
            foreach(var station in catalog.Stations)
            {
                var observer=station.Position+new Vec3d(1800,1700,3200);
                var origin=RenderFrame.ChooseOrigin(observer,Vec3d.Zero);
                world.Position=-ShipTrack.ToVector3(origin);camera.Position=ShipTrack.ToVector3(observer-origin);
                builder.Process(0,observer,origin);camera.Far=builder.RequiredFarPlane;camera.Near=Math.Max(.35f,camera.Far/8000000);
                var node=station.Id==catalog.Station.Id?builder.StationNode:world.GetNode<Node3D>($"Station_{station.Id}");
                if(node.GlobalPosition.DistanceTo(ShipTrack.ToVector3(station.Position-origin))>.02)throw new Exception("Station lost floating-origin precision.");
                if(station.Assembly is { } assembly)
                {
                    for(var i=0;i<assembly.Modules.Count;i++)
                    {
                        var faces=node.GetNode<MeshInstance3D>($"AssemblyModule_{i}").Mesh.GetFaces();
                        var expected=assembly.Modules[i].Triangles().SelectMany(t=>new[]{t.A,t.B,t.C}).ToArray();
                        if(faces.Length!=expected.Length)throw new Exception("Station module triangle count mismatch.");
                        for(var j=0;j<faces.Length;j++)if(faces[j].DistanceTo(ShipTrack.ToVector3(expected[j]))>.001)throw new Exception("Station module render/physics mismatch.");
                    }
                }
                camera.LookAt(node.GlobalPosition,Vector3.Up);
                await Pause(.2);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,$"station_{station.Id}_{station.Purpose}.png"));
            }
            foreach(var sector in catalog.Sectors.Where(s=>s.Id.StartsWith("resource-")))
            {
                var observer=sector.Center+new Vec3d(0,5000,10000);var origin=RenderFrame.ChooseOrigin(observer,Vec3d.Zero);
                world.Position=-ShipTrack.ToVector3(origin);camera.Position=ShipTrack.ToVector3(observer-origin);builder.Process(0,observer,origin);
                if(builder.LoadedAsteroidSectors==0)throw new Exception("Distant resource pocket must stream in.");
                var body=sector.Bodies.First(b=>b.Kind==BodyKind.Asteroid);var node=world.GetNode<Node3D>(sector.Id);
                var mesh=node.GetNode<MultiMeshInstance3D>($"AsteroidBelt_{body.Variant}");
                var shown=node.GlobalPosition+mesh.Multimesh.GetInstanceTransform(0).Origin;
                if(shown.DistanceTo(ShipTrack.ToVector3(body.Position-origin))>.05)throw new Exception("Asteroid render/collision offset mismatch.");
                camera.LookAt(ShipTrack.ToVector3(sector.Center-origin),Vector3.Up);
                await Pause(.1);
            }
            var layer=new CanvasLayer();AddChild(layer);
            var map=new TacticalView {Catalog=catalog,PlayerPosition=new Vec3d(0,0,0)};layer.AddChild(map);map.Visible=true;map.ShowSystem();map._Process(2);map.QueueRedraw();
            await Pause(.2);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"system_map.png"));
            if(catalog.Celestials.Count<7)throw new Exception("Generated bodies required.");
            GD.Print($"SEEDED SYSTEM CHECKS PASSED: {catalog.Celestials.Count} bodies, shaders, map and captures.");GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
}
