using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.Collision;

public sealed partial class CameraChecks : Node
{
    public override async void _Ready()
    {
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_CAMERA_CAPTURE")??"/tmp/indifferent-camera";
            Directory.CreateDirectory(output);
            var game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play"))
                .EmitSignal(Button.SignalName.Pressed);
            ShipView? pilot=null;
            await Until(()=>
            {
                pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);
                return pilot is not null;
            },12,"Connected pilot required.");
            var camera=game.GetNode<Camera3D>("ViewCamera");
            Tap(Key.F2);await Pause(.2);Wheel(MouseButton.WheelUp,80);await Pause(1);
            Check(Mathf.Abs(Distance()-GameMain.MinimumCameraDistance)<.05f,"Near zoom must stop outside the complete hull.");
            await Capture("zoom_near");
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=true});
            Input.ParseInputEvent(new InputEventMouseMotion {Relative=new Vector2(620,35),ButtonMask=MouseButtonMask.Middle});
            await Pause(.05);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=false});
            for(var i=0;i<20;i++)
            {
                await Pause(.03);
                Check(Distance()>=GameMain.MinimumCameraDistance-.05f,"A rapid half-turn must follow an arc outside the hull.");
            }
            var orbit=Field<Vector2>("_cameraOrbit");Check(Mathf.Abs(orbit.X)>2,"Middle-button drag must orbit the camera.");
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=true});
            Input.ParseInputEvent(new InputEventMouseMotion {Relative=new Vector2(-620,105),ButtonMask=MouseButtonMask.Middle});
            await Pause(.05);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Middle,Pressed=false});
            await Pause(.8);orbit=Field<Vector2>("_cameraOrbit");
            var target=Field<float>("_targetCameraDistance");
            menu.ShowMenu(true);Wheel(MouseButton.WheelDown,20);await Pause(.2);
            Check(Field<float>("_targetCameraDistance")==target,"Menu wheel must not zoom the flight camera.");menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("resume")).EmitSignal(Button.SignalName.Pressed);
            Tap(Key.F1);await Pause(.1);Wheel(MouseButton.WheelDown,3);await Pause(.1);
            Check(Field<float>("_targetCameraDistance")==target,"Map wheel must not change external distance.");
            Tap(Key.F3);await Pause(.1);Wheel(MouseButton.WheelDown,3);await Pause(.1);
            Check(Field<float>("_targetCameraDistance")==target,"Cockpit wheel must not change external distance.");
            Check(camera.GlobalPosition.DistanceTo(pilot!.GlobalTransform*CockpitView.EyePosition)<.002f,"Zoom must not displace the cockpit eye.");
            Tap(Key.F2);await Pause(.1);Wheel(MouseButton.WheelDown,80);await Pause(1.3);
            Check(Mathf.Abs(Distance()-GameMain.MaximumCameraDistance)<.05f,"Far zoom must remain bounded.");await Capture("zoom_far");
            Tap(Key.F1);await Pause(.1);Tap(Key.F3);await Pause(.1);Tap(Key.F2);await Pause(.1);
            Check(Mathf.Abs(Distance()-GameMain.MaximumCameraDistance)<.05f && Field<Vector2>("_cameraOrbit").DistanceTo(orbit)<.001f,
                "View switches must preserve external distance and orbit.");
            SetKey(Key.R,true);
            await Until(()=>pilot!.Track.WorldPosition.Y>ModelPose.StationDockingOffset.Y+1500,20,"Departure must clear the station before warp.");
            SetKey(Key.R,false);SetKey(Key.Space,true);await Pause(1.4);SetKey(Key.Space,false);
            var transport=Field<NetworkClient>("_net");
            transport.RequestWarp(0,new Vec3d(0,100000,100000));
            await Until(()=>pilot!.Track.WarpStage==WarpStage.Travel,5,"Safe-space warp must begin.");
            Check(Mathf.Abs(Distance()-GameMain.MaximumCameraDistance)<.1f,"Warp must preserve far zoom.");
            await Until(()=>pilot!.Track.WarpStage==WarpStage.None,7,"Safe-space warp must finish.");
            Tap(Key.V);await Until(()=>pilot!.Track.Cruising,3,"Cruise must engage in clear space.");
            Directory.CreateDirectory(Path.Combine(output,"zoom"));
            for(var i=0;i<45;i++)
            {
                Wheel(MouseButton.WheelUp,1);await Pause(.05);await Capture($"zoom/{i:D4}");
                Check(Distance()>=GameMain.MinimumCameraDistance-.05f && Distance()<=GameMain.MaximumCameraDistance+.05f,
                    "Zoom must remain bounded during cruise and rebasing.");
            }
            await Pause(.7);
            Check(Mathf.Abs(Distance()-GameMain.MinimumCameraDistance)<.05f,"Cruise must retain near zoom.");
            Tap(Key.V);
            GD.Print("CAMERA CHECKS PASSED: wheel bounds, middle orbit, safe arc, menus, F1/F2/F3, cockpit eye, warp, cruise and rebasing.");
            await game.QuitGracefully();
            float Distance()=>camera.GlobalPosition.DistanceTo(pilot!.GlobalPosition);
            T Field<T>(string name)=>(T)typeof(GameMain).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(game)!;
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture required.");
            }
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
    private static void Wheel(MouseButton button,int count)
    {
        for(var i=0;i<count;i++)Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=button,Pressed=true,Position=new Vector2(800,450)});
    }
    private static void SetKey(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});
        Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});
    }
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> condition,double seconds,string message)
    {
        var end=Time.GetTicksMsec()+(ulong)(seconds*1000);
        while(!condition()&&Time.GetTicksMsec()<end)await Pause(.05);
        Check(condition(),message);
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
