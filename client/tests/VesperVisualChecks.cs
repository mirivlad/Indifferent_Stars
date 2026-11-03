using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

public sealed partial class VesperVisualChecks : Node3D
{
    private readonly List<double> _frames=new();
    private bool _measure;
    private ulong _lastFrame;
    public override void _Process(double delta){var now=Time.GetTicksUsec();if(_measure&&_lastFrame!=0)_frames.Add((now-_lastFrame)/1000.0);_lastFrame=now;}
    public override async void _Ready()
    {
        try
        {
            Locale.Load("ru");
            var baseline=System.Environment.GetEnvironmentVariable("IS_VESPER_BASELINE")=="1";
            var output=System.Environment.GetEnvironmentVariable("IS_VESPER_CAPTURE")??"/tmp/vesper-candidate";
            Directory.CreateDirectory(output);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);Engine.MaxFps=0;
            GetViewport().Scaling3DScale=1;GetViewport().Msaa3D=Viewport.Msaa.Msaa2X;
            var camera=new Camera3D {Current=true,Fov=60,Near=.05f,Far=400000};AddChild(camera);
            var root=new Node3D();AddChild(root);
            var catalog=WorldCatalog.Generate(GameMain.DefaultSeed);
            var builder=new WorldBuilder(root,catalog,true);builder.Build();
            var star=root.GetNode<Node3D>("CelestialBodies/StarVesper");
            var environment=root.GetChildren().OfType<WorldEnvironment>().Single().Environment;
            var light=root.GetNode<DirectionalLight3D>("Starlight");
            var center=catalog.CelestialOffset(catalog.Star);
            var radial=new Vec3d(1,.2,.15).Normalized();
            var telemetry=new List<object>();
            foreach(var shot in new[]{("far",149597870700d,0f),("mid",catalog.Star.Radius*3,0f),("approach",catalog.Star.Radius*1.3,58f),("near",catalog.Star.Radius+1000,85f)})
            {
                var observer=center+radial*shot.Item2;root.Position=-ShipTrack.ToVector3(observer);
                camera.Position=Vector3.Zero;camera.LookAt(ShipTrack.ToVector3(-radial));camera.RotateObjectLocal(Vector3.Up,Mathf.DegToRad(shot.Item3));
                for(var i=0;i<100;i++)builder.Process(.05,observer,observer);
                SetTime(star,12);camera.Far=builder.RequiredFarPlane;
                await Pause(.5);await Capture(shot.Item1);
                if(shot.Item1 is "mid" or "approach")
                {
                    var ship=new ShipView(10000,true,"Vesper observer",true);AddChild(ship);
                    var shipOffset=camera.GlobalBasis*new Vector3(0,-3,-38);var q=camera.GlobalBasis.GetRotationQuaternion();
                    ship.Track.Push(new EntitySnapshot(10000,EntityKind.Player,"Vesper observer",observer+new Vec3d(shipOffset.X,shipOffset.Y,shipOffset.Z),0,0,Vec3d.Zero,0,
                        new System.Numerics.Quaternion(q.X,q.Y,q.Z,q.W)),0,1);ship.UpdateVisual(observer,.016f);
                    await Pause(.2);await Capture(shot.Item1+"_ship");
                    var previous=camera.Transform;
                    var cockpit=new CockpitView();ship.AddChild(cockpit);ship.SetExternalModelVisible(false);
                    cockpit.UpdateInstruments(0,0,false,"Vesper",(float)(shot.Item2-catalog.Star.Radius),"Vesper",shot.Item2-catalog.Star.Radius,true,false,false,false);
                    camera.GlobalTransform=new Transform3D(ship.GlobalBasis,ship.GlobalPosition+ship.GlobalBasis*CockpitView.EyePosition);
                    builder.Process(0,observer,observer);SetTime(star,12);await Pause(.2);await Capture(shot.Item1+"_cockpit");
                    camera.Transform=previous;ship.QueueFree();builder.Process(0,observer,observer);SetTime(star,12);await Pause(.1);
                }
                if(shot.Item1 is "approach" or "near")
                {
                    _frames.Clear();_measure=true;await Pause(1.5);_measure=false;
                    var measured=_frames.OrderBy(v=>v).ToArray();
                    var result=new {shot=shot.Item1,visible=true,frames=measured.Length,p50_ms=measured[measured.Length/2],p95_ms=measured[(int)(measured.Length*.95)],exposure=environment.TonemapExposure,light=light.LightEnergy};
                    telemetry.Add(result);GD.Print(JsonSerializer.Serialize(result));
                    camera.RotateObjectLocal(Vector3.Up,Mathf.Pi);builder.Process(.016,observer,observer);SetTime(star,12);await Pause(.1);await Capture(shot.Item1+"_away");
                    camera.RotateObjectLocal(Vector3.Up,Mathf.Pi);builder.Process(0,observer,observer);SetTime(star,12);
                    if(shot.Item1=="near")
                    {
                        camera.LookAt(ShipTrack.ToVector3(-radial));
                        for(var i=0;i<100;i++)builder.Process(.05,observer,observer);
                        SetTime(star,12);await Pause(.2);await Capture("near_face");
                        _frames.Clear();_measure=true;await Pause(1.5);_measure=false;
                        var faceTimes=_frames.OrderBy(v=>v).ToArray();
                        var faceResult=new {shot="near_face",frames=faceTimes.Length,p95_ms=faceTimes[(int)(faceTimes.Length*.95)],exposure=environment.TonemapExposure};
                        telemetry.Add(faceResult);GD.Print(JsonSerializer.Serialize(faceResult));
                    }
                }
                if(shot.Item1=="mid")
                {
                    if(!baseline)
                    {
                        var surface=star.GetNode<MeshInstance3D>("Photosphere");var corona=star.GetNode<MeshInstance3D>("Corona");
                        var arcs=star.GetChildren().OfType<MeshInstance3D>().Where(m=>m.Name.ToString().StartsWith("Prominence_")).ToArray();
                        Check(arcs.Length==12,"Four spatial prominences must have three plasma strands each.");
                        surface.Visible=false;foreach(var arc in arcs)arc.Visible=false;
                        var material=(ShaderMaterial)corona.MaterialOverride;
                        material.SetShaderParameter("layer_mask",1);await Pause(.1);var chromosphere=await Capture("layer_chromosphere");
                        material.SetShaderParameter("layer_mask",2);await Pause(.1);var coronaImage=await Capture("layer_corona");
                        var changed=0;
                        for(var y=120;y<780;y+=8)for(var x=400;x<1200;x+=8)
                            if(Math.Abs(chromosphere.GetPixel(x,y).R-coronaImage.GetPixel(x,y).R)>.01)changed++;
                        Check(changed>100,"Chromosphere and volumetric corona must produce distinct visible layers.");
                        material.SetShaderParameter("layer_mask",3);corona.Visible=false;
                        foreach(var arc in arcs)arc.Visible=true;await Pause(.1);await Capture("layer_prominences");
                        corona.Visible=true;surface.Visible=true;
                    }
                    foreach(var visible in new[]{false,true})
                    {
                        star.Visible=visible;await Pause(.5);_frames.Clear();_measure=true;await Pause(2);_measure=false;
                        var times=_frames.OrderBy(x=>x).ToArray();
                        var result=new {shot="mid",visible,frames=times.Length,p50_ms=times[times.Length/2],p95_ms=times[(int)(times.Length*.95)],exposure=environment.TonemapExposure,light=light.LightEnergy};
                        telemetry.Add(result);GD.Print(JsonSerializer.Serialize(result));
                    }
                    SetTime(star,12);builder.SetGlowEnabled(false);await Pause(.1);await Capture("mid_low");
                    _frames.Clear();_measure=true;await Pause(1.5);_measure=false;
                    var lowTimes=_frames.OrderBy(v=>v).ToArray();
                    telemetry.Add(new {shot="mid_low",frames=lowTimes.Length,p95_ms=lowTimes[(int)(lowTimes.Length*.95)]});
                    builder.SetGlowEnabled(true);
                    var baseTransform=camera.Transform;
                    var before=light.LightEnergy;var beforeExposure=environment.TonemapExposure;
                    camera.RotateObjectLocal(Vector3.Up,Mathf.Pi);
                    builder.Process(.016,observer,observer);SetTime(star,12);await Pause(.1);await Capture("look_away");
                    Check(Math.Abs(light.LightEnergy-before)<.001,"Stellar illumination must not depend on camera bearing.");
                    Check(Math.Abs(environment.TonemapExposure-beforeExposure)<.03,"Exposure must not jump on a camera turn.");
                    camera.Transform=baseTransform;
                    for(var i=0;i<32;i++){builder.Process(.12,observer,observer);await Capture($"motion/{i:D3}");}
                    for(var i=0;i<32;i++)
                    {
                        var angle=i*.025;
                        var orbit=new Vec3d(Math.Cos(angle),.2,Math.Sin(angle)).Normalized();var at=center+orbit*shot.Item2;
                        root.Position=-ShipTrack.ToVector3(at);camera.Position=Vector3.Zero;camera.LookAt(ShipTrack.ToVector3(-orbit));
                        builder.Process(0,at,at);SetTime(star,12);await Pause(.15);await Capture($"orbit/{i:D3}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(output,"telemetry.json"),JsonSerializer.Serialize(telemetry,new JsonSerializerOptions{WriteIndented=true}));
            GD.Print("VESPER VISUAL CHECKS PASSED");GetTree().Quit();
            async Task<Image> Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var path=Path.Combine(output,name+".png");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var image=GetViewport().GetTexture().GetImage();Check(image.SavePng(path)==Error.Ok,"Capture must save.");return image;
            }
        }
        catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}
    }
    private static void SetTime(Node3D star,float time)
    {
        foreach(var mesh in star.GetChildren().OfType<MeshInstance3D>())if(mesh.MaterialOverride is ShaderMaterial material)material.SetShaderParameter("animation_time",time);
    }
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
