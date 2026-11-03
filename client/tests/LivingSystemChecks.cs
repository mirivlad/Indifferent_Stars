using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Simulation;
using IndifferentStars.Shared.Protocol;

public sealed partial class LivingSystemChecks : Node
{
    private NetworkClient? _observer;
    private readonly Dictionary<uint,NpcStatus> _observed=new();
    private ChatLineMessage? _observerSignal;
    private readonly List<double> _frames=new();
    public override void _Process(double delta)
    {
        _observer?.Poll();_observer?.Update(delta,InputState.None);
        if(_frames.Count<50000)_frames.Add(delta*1000);
    }
    public override async void _Ready()
    {
        GameMain? game=null;
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_LIFE_CAPTURE")??"/tmp/indifferent-life";Directory.CreateDirectory(output);
            game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            var net=Field<NetworkClient>(game,"_net");ShipView? pilot=null;
            await Until(()=>{pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);return pilot is not null;},12,"Pilot must connect.");
            var endpoint=(System.Environment.GetEnvironmentVariable("IS_SERVER")??"127.0.0.1:17879").Split(':');
            _observer=new NetworkClient(endpoint[0],int.Parse(endpoint[1]),"Life observer");
            _observer.SnapshotReceived+=snapshot=>{foreach(var ship in snapshot.Entities)if(ship.Activity is { } work)_observed[ship.Id]=work;};
            _observer.ChatReceived+=line=>{if(line.SignalPosition.HasValue)_observerSignal=line;};_observer.Start();
            ChatLineMessage? signal=null;net.ChatReceived+=line=>{if(line.SignalPosition.HasValue)signal=line;};
            await Until(()=>_observer.State==ConnectionState.Connected&&_observed.Count>0,12,"Second client must receive NPC activities.");
            _frames.Clear();
            var actor=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().First(s=>!s.IsPlayer&&s.Track.Activity is not null);
            await Until(()=>_observed.TryGetValue(actor.Id,out var status)&&status.Occupation==actor.Track.Activity!.Occupation,5,"Both clients must agree on occupation.");
            var radio=game.GetNode<RadioChat>("RadioChat");var tabs=(TabBar)radio.FindChild("RadioTabs",true,false);tabs.CurrentTab=1;
            SetTarget(game,actor.Id);typeof(GameMain).GetMethod("ShowObjectInformation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null);
            await Pause(.2);await Capture("npc_information");
            ((Control)Field<NavigationPanel>(game,"_navigation").FindChild("ObjectInformation",true,false)).Hide();
            KeyPress(Key.F1);KeyPress(Key.M);await Pause(1);await Capture("populated_map");
            KeyPress(Key.F2);await Pause(1);await Capture("living_external");
            KeyPress(Key.F3);await Pause(1);await Capture("living_cockpit");
            await Until(()=>signal is not null&&_observerSignal is not null,150,"A real survey/fault event must arise in the running simulation.");
            Check(signal!.MessageId==_observerSignal!.MessageId&&Nullable.Equals(signal.SignalPosition,_observerSignal.SignalPosition),"Both clients must receive the same signal location.");
            tabs.CurrentTab=1;await Pause(.2);await Capture("simulation_signal");
            var history=(RichTextLabel)radio.FindChild("RadioHistory",true,false);
            Check(history.GetParsedText().Contains(Locale.Text("signal_position")),"System event must expose a signal position action.");
            history.EmitSignal(RichTextLabel.SignalName.MetaClicked,0);
            var context=Field<PopupMenu>(game,"_contactMenu");Check(context.Visible&&context.GetItemIndex(23)>=0,"Signal must open the waypoint context menu.");
            context.Hide();context.EmitSignal(PopupMenu.SignalName.IdPressed,23L);
            await Until(()=>!net.IsRoutePending,4,"Signal route plan must be acknowledged.");
            Check(!pilot!.Track.RouteActive,"Planning a signal visit must not start autopilot.");
            var ordered=_frames.OrderBy(x=>x).ToArray();
            GD.Print($"LIFE FRAME SAMPLE: frames={ordered.Length}, p95={ordered[(int)(ordered.Length*.95)]:F2} ms, 1280x720, Forward+, default VSync, two network clients / one renderer.");
            GD.Print($"LIVING SYSTEM CHECKS PASSED: two clients, NPC status, real event #{signal.MessageId} from {signal.Sender}, actionable position, three views and route planning.");
            _observer.Dispose();_observer=null;await game.QuitGracefully();
            async Task Capture(string name)
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture failed.");}
        }
        catch(Exception error){GD.PrintErr(error);_observer?.Dispose();_observer=null;if(game is not null)await game.QuitGracefully(1);else GetTree().Quit(1);}
    }
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void SetTarget(GameMain game,uint id)=>typeof(GameMain).GetField("_targetId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(game,id);
    private static void KeyPress(Key key){Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});}
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> condition,double seconds,string message){var end=Time.GetTicksMsec()+(ulong)(seconds*1000);while(!condition()&&Time.GetTicksMsec()<end)await Pause(.05);Check(condition(),message);}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
