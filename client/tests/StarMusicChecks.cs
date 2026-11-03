using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using IndifferentStars.Shared.Protocol;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

public sealed partial class StarMusicChecks : Node3D
{
    private readonly List<double> _frames=new();
    private bool _measure;
    public override void _Process(double delta){if(_measure)_frames.Add(delta*1000);}
    public override async void _Ready()
    {
        try
        {
            Locale.Load("ru");
            Check(NavigationPanel.Distance(148_894_400_000)=="1,00 а.е.","Large navigation distances must use AU with Russian decimal comma.");
            Check(NavigationPanel.Distance(49_890_000_000)=="49,89 млн км","Sub-half-AU distances must use million km.");
            Check(NavigationPanel.Distance(149_597_870_700*.5)=="0,50 а.е.","AU boundary must start at half an AU.");
            Check(NavigationPanel.Distance(999).EndsWith(" м")&&NavigationPanel.Distance(1000)=="1,0 км"&&NavigationPanel.Distance(1e6)=="1,0 тыс. км","Small distance units must be retained.");
            Locale.Load("en");Check(NavigationPanel.Distance(49_890_000_000)=="49.89 million km","English distances must retain a decimal point.");Locale.Load("ru");
            var paths=ShipAudio.GetBundledMusicPaths();
            var sources=Directory.GetFiles(ProjectSettings.GlobalizePath("res://assets/music"),"*.mp3");
            Check(paths.Length==20&&paths.Length==sources.Length,"Every bundled MP3 must be in the playlist.");
            var audio=new ShipAudio();AddChild(audio);
            var player=audio.GetNode<AudioStreamPlayer>("AmbientMusic");
            for(var i=0;i<paths.Length;i++)
            {
                var stream=player.Stream as AudioStreamMP3;
                Check(stream is not null&&stream.ResourcePath==paths[i]&&stream.GetLength()>0&&!stream.Loop,"Each song must load, play in order and finish without looping itself.");
                Check(player.Playing,"Music player must be playing.");
                player.EmitSignal(AudioStreamPlayer.SignalName.Finished);
                await Pause(.015);
            }
            Check(player.Stream.ResourcePath==paths[0],"Playlist must wrap only after every track.");
            var previous=player.Stream;audio.SetMusicDirectory("/nonexistent-indifferent-music");
            Check(player.Stream==previous&&player.Playing,"Changing directory must not interrupt current song.");
            audio.Stop();audio.QueueFree();
            GD.Print($"MUSIC CHECKS PASSED: {paths.Length} imported tracks, complete sequence, wrap, no per-track loops.");

            var output=System.Environment.GetEnvironmentVariable("IS_STAR_CAPTURE")??"/tmp/indifferent-star";Directory.CreateDirectory(output);
            var environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=Colors.Black,
                GlowEnabled=true,GlowIntensity=.55f,GlowStrength=.9f,GlowBloom=.06f,GlowHdrThreshold=1.1f};
            AddChild(new WorldEnvironment {Environment=environment});
            var camera=new Camera3D {Current=true,Fov=60,Near=.05f,Far=400_000};AddChild(camera);
            var root=new Node3D();AddChild(root);
            var catalog=WorldCatalog.Generate(GameMain.DefaultSeed);
            var renderer=new CelestialRenderer(root,catalog,true);renderer.PlanetHolder.Visible=false;
            var star=root.GetNode<Node3D>("CelestialBodies/StarVesper");
            Check(star.GetNode<MeshInstance3D>("Photosphere").MaterialOverride is ShaderMaterial material&&material.Shader.ResourcePath.EndsWith("/star.gdshader"),"The actual stellar surface must use the star shader.");
            Check(star.GetNode<MeshInstance3D>("Corona").MaterialOverride is ShaderMaterial,"Corona must use an animated shader.");
            var center=catalog.CelestialOffset(catalog.Star);
            var direction=new Vec3d(1,.2,.15).Normalized();
            foreach(var (name,distance) in new[]{("01_one_au",149_597_870_700d),("02_three_radii",catalog.Star.Radius*3),("04_near_surface",catalog.Star.Radius+1000)})
            {
                var observer=center+direction*distance;
                camera.Position=Vector3.Zero;camera.LookAt(ShipTrack.ToVector3(-direction));
                renderer.Update(observer,observer,.016f);camera.Far=renderer.RequiredFarPlane;
                Check(camera.Far<=800_000,"Approaching a star must not expand the foreground camera frustum.");
                await Pause(.2);var before=await Capture(name);
                var middle=before.GetPixel(before.GetWidth()/2,before.GetHeight()/2);
                Check(middle.R>.15f,"Star must stay visible at the physical bearing, including 1 km above the surface.");
                if(name=="02_three_radii")
                {
                    var corona=star.GetNode<MeshInstance3D>("Corona");corona.Visible=false;environment.GlowEnabled=false;
                    await Pause(.1);var disc=await Capture("02_physical_disc");
                    var width=Enumerable.Range(0,disc.GetWidth()).Count(x=>disc.GetPixel(x,disc.GetHeight()/2).R>.1);
                    var expected=disc.GetHeight()*Math.Tan(Math.Asin(catalog.Star.Radius/distance))/Math.Tan(Mathf.DegToRad(camera.Fov)/2);
                    Check(Math.Abs(width-expected)<5,$"Physical disc diameter mismatch: {width} vs {expected:F2} pixels.");
                    corona.Visible=true;environment.GlowEnabled=true;
                    renderer.Update(observer,observer,8);await Pause(.1);var after=await Capture("03_animated");
                    var changed=0;
                    for(var y=250;y<650;y+=8)for(var x=600;x<1000;x+=8)
                        if(Math.Abs(before.GetPixel(x,y).R-after.GetPixel(x,y).R)+Math.Abs(before.GetPixel(x,y).G-after.GetPixel(x,y).G)>.01)changed++;
                    Check(changed>50,"Stellar surface must visibly animate.");
                    var photosphere=star.GetNode<MeshInstance3D>("Photosphere");photosphere.Visible=false;environment.GlowEnabled=false;
                    await Pause(.1);var coronaBefore=await Capture("03_corona_only");
                    renderer.Update(observer,observer,8);await Pause(.1);var coronaAfter=await Capture("03_corona_animated");
                    var lit=0;var moving=0;
                    for(var y=50;y<850;y+=4)for(var x=200;x<1400;x+=4)
                    {
                        var a=coronaBefore.GetPixel(x,y);var b=coronaAfter.GetPixel(x,y);
                        if(a.R>.05)lit++;
                        if(Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)>.02)moving++;
                    }
                    Check(lit>100&&moving>30,"Corona must visibly render and animate beyond shader attachment.");
                    photosphere.Visible=true;
                    environment.GlowEnabled=false;await Pause(.1);await Capture("03_low_no_bloom");environment.GlowEnabled=true;
                    for(var i=0;i<12;i++) {renderer.Update(observer,observer,.5f);await Capture($"motion/{i:D3}");}
                    foreach(var visible in new[]{false,true})
                    {
                        star.Visible=visible;await Pause(.5);_frames.Clear();_measure=true;await Pause(3);_measure=false;
                        var times=_frames.OrderBy(v=>v).ToArray();
                        GD.Print($"STAR FRAME TELEMETRY visible={visible} samples={times.Length} p95_ms={times[(int)(times.Length*.95)]:F3} viewport={GetViewport().GetVisibleRect().Size} MSAA={GetViewport().Msaa3D}");
                    }
                    var ship=new ShipView(10000,true,"Star observer",true);AddChild(ship);
                    var shipPosition=camera.Basis*new Vector3(0,-3,-40);
                    var rotation=camera.Basis.GetRotationQuaternion();
                    ship.Track.Push(new EntitySnapshot(10000,EntityKind.Player,"Star observer",observer+new Vec3d(shipPosition.X,shipPosition.Y,shipPosition.Z),0,0,Vec3d.Zero,0,
                        new System.Numerics.Quaternion(rotation.X,rotation.Y,rotation.Z,rotation.W)),0,1);
                    ship.UpdateVisual(observer,.016f);
                    await Pause(.2);await Capture("03_external_ship");
                    var cockpit=new CockpitView();ship.AddChild(cockpit);ship.SetExternalModelVisible(false);
                    var cameraTransform=camera.GlobalTransform;
                    camera.GlobalTransform=new Transform3D(ship.GlobalBasis,ship.GlobalPosition+ship.GlobalBasis*CockpitView.EyePosition);
                    renderer.Update(observer,observer,0);await Pause(.2);await Capture("03_cockpit");
                    camera.GlobalTransform=cameraTransform;renderer.Update(observer,observer,0);ship.QueueFree();await Pause(.05);
                }
                var marker=new MeshInstance3D {Mesh=new BoxMesh {Size=new Vector3(2,2,2)},
                    MaterialOverride=new StandardMaterial3D {ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor=Colors.Green},
                    Position=camera.Basis*new Vector3(0,0,-12)};AddChild(marker);
                await Pause(.1);var occluded=await Capture(name+"_foreground");
                var pixel=occluded.GetPixel(occluded.GetWidth()/2,occluded.GetHeight()/2);
                Check(pixel.G>pixel.R*2,"Foreground geometry must occlude the star and corona.");
                marker.QueueFree();await Pause(.05);
                camera.RotateY(Mathf.Pi);renderer.Update(observer,observer,0);await Pause(.1);
                var away=await Capture(name+"_away");
                Check(away.GetPixel(away.GetWidth()/2,away.GetHeight()/2).R<.05f,"Star must disappear when looking away, without a rear-facing halo.");
            }
            var polar=center+Vec3d.Up*catalog.Star.Radius*3;
            camera.Position=Vector3.Zero;camera.LookAt(Vector3.Down,Vector3.Forward);renderer.Update(polar,polar,.016f);
            await Pause(.2);await Capture("05_polar");
            GD.Print("STAR CHECKS PASSED: shader binding, physical angular size, far/close, animation, Low, polar view, foreground occlusion and bounded frustum.");
            GetTree().Quit();
            async Task<Image> Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var image=GetViewport().GetTexture().GetImage();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(output,name+".png"))!);
                Check(image.SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture must save.");return image;
            }
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
