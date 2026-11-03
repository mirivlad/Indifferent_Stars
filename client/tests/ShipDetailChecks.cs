using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Simulation;
using IndifferentStars.Shared.World;

public sealed partial class ShipDetailChecks : Node3D
{
    private readonly List<double> _frames = new();
    private ulong _lastFrame;
    private bool _measure;

    public override void _Process(double delta)
    {
        var now=Time.GetTicksUsec();
        if(_measure && _lastFrame!=0) _frames.Add((now-_lastFrame)/1000.0);
        _lastFrame=now;
    }

    public override async void _Ready()
    {
        try
        {
            CollisionAssets.Validate();
            var output=System.Environment.GetEnvironmentVariable("IS_SHIP_CAPTURE") ?? "/tmp/indifferent-ship";
            Directory.CreateDirectory(output);
            GetViewport().Scaling3DScale=1;
            GetViewport().Msaa3D=Viewport.Msaa.Msaa4X;
            var catalog=WorldCatalog.Generate(GameMain.DefaultSeed);
            var builder=new WorldBuilder(this,catalog,true);builder.Build();
            builder.SetVisualQuality(true,2);
            var world=new GameWorld(catalog);
            var player=world.AddPlayer("Wren");
            player.Position=new Vec3d(0,2200,3300);
            player.Orientation=System.Numerics.Quaternion.Identity;
            builder.Process(0,player.Position,Vec3d.Zero,0);
            ShipView own=null!;
            foreach(var snapshot in world.BuildSnapshot())
            {
                var ship=new ShipView(snapshot.Id,snapshot.Id==player.Id,snapshot.Name,true);
                AddChild(ship);
                ship.Track.Push(snapshot,0,0);ship.UpdateVisual(Vec3d.Zero,.05f,0);
                if(snapshot.Id==player.Id) own=ship;
            }
            var model=own.FindChild("GlbModel",true,false) as Node3D;
            Check(model is not null,"Ship model required.");
            var eye=model!.FindChild("PilotEye",true,false) as Node3D;
            Check(eye is not null && eye.GlobalPosition.DistanceTo(own.Position+CockpitView.EyePosition)<.001f,
                "Pilot eye and exterior bridge must match.");
            var eyePosition=CockpitView.EyePosition;
            Check(SurfaceQuery.Contains(SurfaceMesh.Ship,new Vec3d(eyePosition.X,eyePosition.Y,eyePosition.Z)),
                "Pilot eye must sit inside the bridge shell.");
            var meshes=model.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().ToArray();
            var materials=meshes.SelectMany(mesh=>Enumerable.Range(0,mesh.Mesh.GetSurfaceCount())
                .Select(i=>mesh.Mesh.SurfaceGetMaterial(i))).OfType<BaseMaterial3D>().ToArray();
            Check(materials.Count(mat=>mat.AlbedoTexture is not null)>=7,"Seven textured materials required.");
            Check(materials.Where(mat=>mat.AlbedoTexture is not null).All(mat=>mat.AlbedoTexture.GetImage().HasMipmaps()),"Ship textures need mipmaps.");
            var sockets=model.FindChildren("MainNozzle_*","Node3D",true,false).OfType<Node3D>().OrderBy(n=>n.Name.ToString()).ToArray();
            var glows=own.FindChildren("EngineGlow_*","Node3D",true,false).OfType<Node3D>().OrderBy(n=>n.Name.ToString()).ToArray();
            Check(sockets.Length==2 && glows.Length==2,"Main outlets required.");
            for(var i=0;i<2;i++)Check(sockets[i].GlobalPosition.DistanceTo(glows[i].GlobalPosition)<.001f,"Main exhaust must start at the exported outlet.");
            var camera=new Camera3D {Current=true,Fov=55,Near=.15f,Far=builder.RequiredFarPlane};AddChild(camera);
            var target=own.Position;
            foreach(var (name,offset) in new[] {
                ("00_hero",new Vector3(-16,10,-21)),("01_rear",new Vector3(23,12,28)),
                ("02_side",new Vector3(-32,8,0)),("03_underside",new Vector3(22,-18,-23)),
                ("04_bridge",new Vector3(-7,7,-14)),("05_engines",new Vector3(8,3,20)) })
            {
                camera.Position=target+offset;camera.LookAt(target,Vector3.Up);
                await Pause(.4);await Capture(name);
            }
            camera.Position=target+new Vector3(-8,3,-3);
            camera.LookAt(target+new Vector3(-2.4f,.2f,-2),Vector3.Up);
            await Pause(.4);await Capture("08_surface");
            if(OS.GetCmdlineUserArgs().Contains("--quick"))
            {
                GD.Print("SHIP QUICK CHECKS PASSED: PBR, mipmaps, pilot eye and nozzle anchors.");GetTree().Quit();return;
            }
            camera.Position=target+new Vector3(-16,10,-21);camera.LookAt(target,Vector3.Up);
            foreach(var high in new[]{true,false})
            {
                builder.SetGlowEnabled(high);builder.SetVisualQuality(high,high?2:0);
                await Pause(1);_frames.Clear();_measure=true;await Pause(5);_measure=false;
                var frames=_frames.OrderBy(x=>x).ToArray();
                Check(frames.Length>10,"Measured frames required.");
                GD.Print($"SHIP TELEMETRY: high={high}, frames={frames.Length}, p95={frames[(int)(frames.Length*.95)]:F2}ms, " +
                    $"draw_calls={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}, " +
                    $"video_bytes={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed)}, " +
                    $"texture_bytes={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed)}, viewport={GetViewport().GetVisibleRect().Size}, MSAA={GetViewport().Msaa3D}.");
                await Capture(high?"06_high":"07_low");
            }
            if(System.Environment.GetEnvironmentVariable("IS_SHIP_MOVIE")=="1")
            {
                builder.SetGlowEnabled(true);builder.SetVisualQuality(true,2);
                Directory.CreateDirectory(Path.Combine(output,"orbit"));
                for(var i=0;i<120;i++)
                {
                    var angle=i*Mathf.Tau/120;
                    camera.Position=target+new Vector3(Mathf.Sin(angle)*38,13,Mathf.Cos(angle)*38);
                    camera.LookAt(target,Vector3.Up);await Capture($"orbit/{i:D4}");
                }
            }
            GD.Print("SHIP DETAIL CHECKS PASSED: materials, pilot bridge, exhaust anchors, views and quality.");GetTree().Quit();
            async System.Threading.Tasks.Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture required.");
            }
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
    private async System.Threading.Tasks.Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
