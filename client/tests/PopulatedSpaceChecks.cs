using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

public sealed partial class PopulatedSpaceChecks : Node
{
    private NetworkClient? _observer;
    private readonly Dictionary<uint,SensorContact> _otherContacts=new();
    private ShipView? _fastPilot;
    private Vec3d _previousFastPosition;
    private double _maxFastJump;
    private int _fastFrames;
    public override void _Process(double delta)
    {
        _observer?.Poll(); _observer?.Update(delta,InputState.None);
        if(_fastPilot is null)return;
        if(_fastFrames>0&&delta>0)
            _maxFastJump=Math.Max(_maxFastJump,Vec3d.Distance(_fastPilot.Track.WorldPosition,_previousFastPosition)/(500_000*delta));
        _previousFastPosition=_fastPilot.Track.WorldPosition;_fastFrames++;
    }
    public override async void _Ready()
    {
        GameMain? game=null;
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_POPULATED_CAPTURE")??"/tmp/indifferent-populated";
            Directory.CreateDirectory(output);
            game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            var net=Field<NetworkClient>(game,"_net"); ShipView? pilot=null;
            await Until(()=>{pilot=Field<ShipView?>(game,"_playerShip");return pilot?.Track.HasData==true;},15,"Pilot connection");
            var contacts=Field<Dictionary<uint,SensorContact>>(game,"_sensorContacts");
            await Until(()=>contacts.Count>10,5,"System-wide real transponders");
            var endpoint=(System.Environment.GetEnvironmentVariable("IS_SERVER")??"127.0.0.1:17889").Split(':');
            _observer=new NetworkClient(endpoint[0],int.Parse(endpoint[1]),"Traffic observer");
            _observer.SensorsReceived+=message=>{foreach(var contact in message.Contacts)_otherContacts[contact.Id]=contact;};_observer.Start();
            await Until(()=>_observer.State==ConnectionState.Connected&&_otherContacts.Count>10,8,"Independent observer contacts");
            var distant=contacts.Values.First(c=>Vec3d.Distance(c.Position,pilot!.Track.WorldPosition)>1e9);
            await Until(()=>_otherContacts.TryGetValue(distant.Id,out var contact)&&contact.Name==distant.Name,4,"Shared distant identity");
            var navigation=Field<NavigationPanel>(game,"_navigation");
            var transmitter=(Button)navigation.FindChild("TransponderToggle",true,false);
            transmitter.EmitSignal(Button.SignalName.Toggled,false);
            await Until(()=>!net.IsTransponderPending&&!net.TransponderEnabled,4,"Transponder off acknowledgement");
            await Until(()=>_otherContacts.TryGetValue(pilot!.Id,out var contact)&&!contact.Transponder&&contact.Source==ContactSource.Tactical,4,"Transponder off remains a local sensor contact");
            Field<ConcurrentQueue<(IProtocolMessage,double)>>(net,"_incoming").Enqueue((new TransponderMessage(0,true),Time.GetTicksUsec()/1000000.0));
            await Pause(.1);Check(!net.TransponderEnabled,"A delayed transponder acknowledgement must not roll back state");
            transmitter.EmitSignal(Button.SignalName.Toggled,true);
            await Until(()=>!net.IsTransponderPending&&net.TransponderEnabled,4,"Transponder on acknowledgement");
            Tap(Key.F1); var map=Field<TacticalView>(game,"_tactical");map.ZoomAt(map.Size/2,20_000/map.ZoomRange);
            await Pause(1);await Capture("01_populated_tactical");
            var tabs=(TabBar)navigation.FindChild("NavigationTabs",true,false);
            Check(tabs.CurrentTab==0&&tabs.GetTabTitle(0)==Locale.Text("navigation_ship"),"Ships tab opens first");
            var rows=Field<Dictionary<uint,TreeItem>>(navigation,"_rows");
            Check(rows.Count>0&&rows.Keys.All(id=>contacts.ContainsKey(id)),"Ships tab contains only dynamic contacts");
            tabs.CurrentTab=1;await Pause(.1);
            Check(rows.Count>0&&rows.Keys.All(id=>!contacts.ContainsKey(id)),"Objects tab contains only static objects");
            await Capture("05_objects_tab");tabs.CurrentTab=0;
            var search=(LineEdit)navigation.FindChild("NavigationSearch",true,false);search.Text=Locale.Text("sensor_transponder");search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
            await Pause(.3);await Capture("02_distant_transponders");search.Text="";search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);search.ReleaseFocus();
            if(System.Environment.GetEnvironmentVariable("IS_POPULATED_QUICK")=="1")
            {GD.Print("POPULATED ROSTER CAPTURE PASSED");_observer.Dispose();_observer=null;await game.QuitGracefully();return;}
            var actor=Field<Dictionary<uint,ShipView>>(game,"_remoteShips").Values
                .Where(s=>s.Track.Activity?.Occupation==NpcOccupation.Courier).OrderBy(s=>Vec3d.Distance(s.Track.WorldPosition,pilot!.Track.WorldPosition)).First();
            Set(game,"_targetId",actor.Id);Set(game,"_hasTarget",true);Tap(Key.F2);
            net.RequestApproach(actor.Id,true);
            await Until(()=>Vec3d.Distance(pilot!.Track.WorldPosition,actor.Track.WorldPosition)<300,100,"Visual encounter through authoritative following");
            await Pause(1);await Capture("03_real_npc_external");
            GD.Print($"TRAFFIC ENCOUNTER: NPC={actor.Id}, distance={Vec3d.Distance(pilot!.Track.WorldPosition,actor.Track.WorldPosition):F1}m, elapsed={(double)Time.GetTicksMsec()/1000:F1}s");
            // Actual client input cancels following and leaves the station before cruise.
            Hold(Key.R,true);await Pause(15);Hold(Key.R,false);
            Hold(Key.W,true);Hold(Key.Shift,true);await Pause(28);Hold(Key.W,false);Hold(Key.Shift,false);
            Tap(Key.V);
            await Until(()=>pilot!.Track.Cruising&&pilot.Track.Velocity.Length()>99_000,25,"100 km/s cruise");
            Tap(Key.F3);await Pause(.3);Check(pilot!.Track.Cruising,"F3 preserves cruise");
            Tap(Key.F1);await Pause(.3);Check(pilot.Track.Cruising,"F1 preserves cruise");Tap(Key.F2);
            await Until(()=>pilot.Track.Velocity.Length()>199_000,70,"200 km/s cruise outside inhabited area");
            await Until(()=>pilot.Track.Velocity.Length()>499_000,30,"500 km/s cruise");
            _fastPilot=pilot;await Pause(1);_fastPilot=null;
            Check(_fastFrames>10&&_maxFastJump<2.5,$"High-speed interpolation has a frame jump: frames={_fastFrames}, ratio={_maxFastJump}");
            GD.Print($"HIGH SPEED RENDER: frames={_fastFrames}, max_displacement_to_expected={_maxFastJump:F3}");
            Tap(Key.F1);map.CenterOnPlayer();map.ZoomAt(map.Size/2,150_000/map.ZoomRange);
            await Pause(.5);await Capture("04_open_space");
            var local=Field<Dictionary<uint,ShipView>>(game,"_remoteShips").Values.Count(s=>Vec3d.Distance(s.Track.WorldPosition,pilot.Track.WorldPosition)<150_000);
            Check(local==0,"Open space has no fabricated local ships");Check(contacts.Count>10,"Transponders remain available away from civilization");
            Tap(Key.F2);Tap(Key.V);var speed=pilot.Track.Velocity.Length();await Pause(.5);
            Check(Math.Abs(pilot.Track.Velocity.Length()-speed)<100,"Cruise off retains inertia");
            Hold(Key.Space,true);await Until(()=>pilot.Track.Velocity.Length()<.1,22,"Braking from 500 km/s");Hold(Key.Space,false);
            var request=net.RequestRoute(new[]{new NavigationPoint(distant.Id)},true);var accepted=false;
            net.RouteCompleted+=result=>{if(result.RequestId==request)accepted=result.Accepted;};
            await Until(()=>accepted,4,"Route to known distant ship");net.RequestRoute(Array.Empty<NavigationPoint>(),false);
            GD.Print($"POPULATED SPACE CHECKS PASSED: contacts={contacts.Count}, local={local}, two clients, transponder toggle, actual encounter, 100/200/500 km/s, F1/F2/F3, coast, brake and distant route.");
            _observer.Dispose();_observer=null;await game.QuitGracefully();
            async Task Capture(string name)
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture failed");}
        }
        catch(Exception error){GD.PrintErr(error);_observer?.Dispose();_observer=null;if(game is not null)await game.QuitGracefully(1);else GetTree().Quit(1);}
    }
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,value);
    private static void Hold(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey{Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private static void Tap(Key key){Hold(key,true);Hold(key,false);}
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> condition,double seconds,string message){var end=Time.GetTicksMsec()+(ulong)(seconds*1000);while(!condition()&&Time.GetTicksMsec()<end)await Pause(.05);Check(condition(),message);}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
