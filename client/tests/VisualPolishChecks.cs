using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class VisualPolishChecks : Node3D
{
    private ShipView? _ship;
    private WorldBuilder _builder = null!;
    private Node3D _static = null!;
    private double _elapsed;
    private string _state = "idle";
    private Vec3d _position = new(0,2200,3300);
    private readonly List<double> _frames = new();
    private bool _measure;
    private ulong _last;
    public override void _Process(double delta)
    {
        var now=Time.GetTicksUsec();
        if(_measure && _last!=0) _frames.Add((now-_last)/1000.0);
        _last=now;_elapsed+=delta;
        if(_ship is null)return;
        var speed=_state=="brake"?Math.Max(0,500-(_elapsed%2)*200):_state=="idle"?0:500;
        _ship.Boosting=_state=="boost";
        _ship.Track.Push(new EntitySnapshot(10000,EntityKind.Player,"Polish",_position,0,0,
            new Vec3d(0,0,-speed),(sbyte)(_state is "thrust" or "boost"?127:0),System.Numerics.Quaternion.Identity),
            _elapsed,(ulong)(_elapsed*1000));
        _ship.UpdateVisual(Vec3d.Zero,(float)delta,_elapsed+.05);
    }
    public override async void _Ready()
    {
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_POLISH_CAPTURE")??"/tmp/indifferent-polish";
            Directory.CreateDirectory(output);
            GetViewport().Scaling3DScale=1;GetViewport().Msaa3D=Viewport.Msaa.Msaa4X;
            var catalog=WorldCatalog.Generate(GameMain.DefaultSeed);
            _static=new Node3D {Name="StaticWorld"};AddChild(_static);
            _builder=new WorldBuilder(_static,catalog,true);_builder.Build();_builder.SetVisualQuality(true,2);
            _builder.Process(0,_position,Vec3d.Zero);
            var world=new GameWorld(catalog);var pilot=world.AddPlayer("Polish");
            foreach(var snapshot in world.BuildSnapshot())
            {
                var view=new ShipView(snapshot.Id,snapshot.Id==pilot.Id,snapshot.Name,true);AddChild(view);
                if(snapshot.Id==pilot.Id) {_ship=view;continue;}
                view.Track.Push(snapshot,0,0);view.UpdateVisual(Vec3d.Zero,.05f,0);
            }
            var camera=new Camera3D {Current=true,Fov=55,Near=.15f,Far=_builder.RequiredFarPlane};AddChild(camera);
            camera.Position=ShipTrack.ToVector3(_position)+new Vector3(15,6,26);camera.LookAt(ShipTrack.ToVector3(_position));
            foreach(var high in OS.GetCmdlineUserArgs().Contains("--planet-only")?Array.Empty<bool>():new[]{true,false})
            {
                _builder.SetGlowEnabled(high);_builder.SetVisualQuality(high,high?2:0);
                foreach(var state in new[]{"idle","thrust","boost","brake"})
                {
                    _state=state;await Pause(1);await Capture($"{(high?"high":"low")}_{state}");
                }
                _state="thrust";await Pause(1);_frames.Clear();_measure=true;await Pause(5);_measure=false;
                var sorted=_frames.OrderBy(x=>x).ToArray();
                GD.Print($"POLISH TELEMETRY high={high} frames={sorted.Length} p95_ms={sorted[(int)(sorted.Length*.95)]:F3} " +
                    $"draw_calls={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} " +
                    $"video_bytes={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed)} " +
                    $"texture_bytes={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed)}");
            }
            _builder.SetGlowEnabled(true);_builder.SetVisualQuality(true,2);
            _ship!.SetEngineEffects(false);await Pause(.3);await Capture("effects_off");_ship.SetEngineEffects(true);
            var planet=catalog.CelestialOffset(catalog.Planet);
            foreach(var (name,distance) in new[]{("planet_far",catalog.Planet.Radius*3), ("planet_near",catalog.Planet.Radius+300000)})
            {
                _ship.Visible=false;
                var observer=planet+new Vec3d(.8,.45,1).Normalized()*distance;
                _static.Position=-ShipTrack.ToVector3(observer);_builder.Process(0,observer,observer);
                camera.Position=Vector3.Zero;camera.Far=_builder.RequiredFarPlane;
                camera.LookAt(_static.GetNode<Node3D>("CelestialBodies/PlanetAsterReach").Position);
                await Pause(.5);await Capture(name);
            }
            foreach(var (name,direction) in new[]{("planet_day",new Vec3d(-1,.3,.6)),("planet_pole",new Vec3d(-.3,1,.6))})
            {
                var observer=planet+direction.Normalized()*catalog.Planet.Radius*3;
                _static.Position=-ShipTrack.ToVector3(observer);_builder.Process(0,observer,observer);
                camera.Position=Vector3.Zero;camera.Far=_builder.RequiredFarPlane;
                camera.LookAt(_static.GetNode<Node3D>("CelestialBodies/PlanetAsterReach").Position);
                await Pause(.5);await Capture(name);
            }
            if(System.Environment.GetEnvironmentVariable("IS_POLISH_PLANET_MOVIE")=="1")
            {
                Directory.CreateDirectory(Path.Combine(output,"planet_motion"));
                for(var i=0;i<60;i++)
                {
                    var angle=i*Math.PI*2/60;
                    var observer=planet+new Vec3d(Math.Cos(angle),.25,Math.Sin(angle)).Normalized()*catalog.Planet.Radius*3;
                    _static.Position=-ShipTrack.ToVector3(observer);_builder.Process(0,observer,observer);
                    camera.Position=Vector3.Zero;camera.Far=_builder.RequiredFarPlane;
                    camera.LookAt(_static.GetNode<Node3D>("CelestialBodies/PlanetAsterReach").Position);
                    await Capture($"planet_motion/{i:D4}");
                }
            }
            var rock=catalog.Asteroids[0];
            _static.Position=-ShipTrack.ToVector3(rock.Position);_builder.Process(0,rock.Position,rock.Position);
            camera.Position=new Vector3(-3,1,2)*(float)rock.Radius;camera.Far=_builder.RequiredFarPlane;camera.LookAt(Vector3.Zero);
            await Pause(.3);await Capture("rock_after");
            var instances=_static.FindChildren("AsteroidBelt_*","MultiMeshInstance3D",true,false).OfType<MultiMeshInstance3D>().ToArray();
            var materials=instances.Select(i=>i.MaterialOverride).ToArray();
            foreach(var instance in instances)instance.MaterialOverride=null;
            await Pause(.3);await Capture("rock_before");
            for(var i=0;i<instances.Length;i++)instances[i].MaterialOverride=materials[i];
            _static.Position=Vector3.Zero;_builder.Process(0,_position,Vec3d.Zero);_ship.Visible=true;camera.Far=_builder.RequiredFarPlane;
            if(System.Environment.GetEnvironmentVariable("IS_POLISH_MOVIE")=="1")
            {
                Directory.CreateDirectory(Path.Combine(output,"motion"));
                for(var i=0;i<120;i++)
                {
                    _state=i<30?"idle":i<60?"thrust":i<90?"boost":"brake";
                    var angle=.35f+i*.008f;
                    camera.Position=ShipTrack.ToVector3(_position)+new Vector3(Mathf.Sin(angle)*30,6,Mathf.Cos(angle)*30);
                    camera.LookAt(ShipTrack.ToVector3(_position));await Pause(1.0/24);await Capture($"motion/{i:D4}");
                }
            }
            GD.Print("VISUAL POLISH CAPTURES PASSED: fixed scenes, quality, effect states and planetary distances.");GetTree().Quit();
            async System.Threading.Tasks.Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))!=Error.Ok)throw new IOException(name);
            }
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
    private async System.Threading.Tasks.Task Pause(double time)=>await ToSignal(GetTree().CreateTimer(time),SceneTreeTimer.SignalName.Timeout);
}
