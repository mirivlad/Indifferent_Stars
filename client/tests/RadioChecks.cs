using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

public sealed partial class RadioChecks : Node
{
    private NetworkClient? _other;
    private readonly List<double> _frames=new();
    private bool _measure;
    private ulong _previous;
    public override void _Process(double delta)
    {
        _other?.Poll();_other?.Update(delta,InputState.None);
        var now=Time.GetTicksUsec();if(_measure&&_previous!=0)_frames.Add((now-_previous)/1000.0);_previous=now;
    }
    public override async void _Ready()
    {
        GameMain? game=null;
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_RADIO_CAPTURE")??"/tmp/indifferent-radio";Directory.CreateDirectory(output);
            game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            ShipView? pilot=null;
            await Until(()=>{pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);return pilot is not null;},12,"Connected pilot required.");
            var net=Field<NetworkClient>(game,"_net");var radio=game.GetNode<RadioChat>("RadioChat");
            var input=radio.FindChild("ChatInput",true,false) as LineEdit??throw new InvalidOperationException("Chat entry required.");
            var tabs=radio.FindChild("RadioTabs",true,false) as TabBar??throw new InvalidOperationException("Tabs required.");
            var history=radio.FindChild("RadioHistory",true,false) as RichTextLabel??throw new InvalidOperationException("History required.");
            var args=OS.GetCmdlineUserArgs();var endpoint=args[Array.IndexOf(args,"--server")+1].Split(':');
            _other=new NetworkClient(endpoint[0],int.Parse(endpoint[1]),"Wingmate");
            var received=new List<ChatLineMessage>();_other.ChatReceived+=received.Add;_other.Start();
            await Until(()=>_other.State==ConnectionState.Connected,5,"Second client required.");
            var position=pilot!.Track.WorldPosition;var orientation=pilot.Track.Orientation;
            var target=Field<uint>(game,"_targetId");
            SetKey(Key.Tab,true);SetKey(Key.Tab,false);await Pause(.1);
            Check(!radio.IsTyping && Field<uint>(game,"_targetId")!=target,"Tab must cycle flight targets rather than focus chat.");
            var mouseMode=Input.MouseMode;
            SetKey(Key.Alt,true);await Pause(.1);
            Check(Input.MouseMode==Input.MouseModeEnum.Captured,"Alt must begin camera look.");
            SetKey(Key.Enter,true);SetKey(Key.Enter,false);await Pause(.1);
            Check(radio.IsTyping,"Enter must open chat.");
            SetKey(Key.Alt,false);await Pause(.1);
            Check(Input.MouseMode==Input.MouseModeEnum.Visible,"Chat must release camera look.");
            SetKey(Key.W,true);await Pause(.4);SetKey(Key.W,false);
            Check(pilot.Track.Velocity.Length()<.1f && Mathf.Abs(pilot.Track.Orientation.Dot(orientation))>.99999f,
                "Typing must not steer or accelerate the ship.");
            input.Text="Проверка [b]WASD[/b] — это текст";SetKey(Key.Enter,true);SetKey(Key.Enter,false);
            await Until(()=>received.Count>0,3,"Local message must reach the second client.");
            await Until(()=>history.GetParsedText().Contains("[b]WASD[/b]"),3,"Markup must remain plain text.");
            Check(!radio.IsTyping,"Submitting must release flight controls.");
            Check(Input.MouseMode==mouseMode,"Leaving chat must restore the mouse mode from before temporary camera look.");
            var first=received[0];var text=history.GetParsedText();
            Field<ConcurrentQueue<(IProtocolMessage,double)>>(net,"_incoming").Enqueue((first,Time.GetTicksUsec()/1000000.0));
            await Pause(.15);Check(history.GetParsedText()==text,"Repeated UDP delivery must not duplicate history.");
            _other.SendChat(RadioChannels.General,"Общий канал: связь с пилотом установлена.");
            await Until(()=>tabs.GetTabTitle(2).Contains("(1)"),3,"Inactive channel must show unread count.");
            tabs.CurrentTab=2;await Pause(.1);Check(!tabs.GetTabTitle(2).Contains("("),"Selecting a channel clears unread count.");
            await Capture("01_general");
            await Pause(.6);_other.SendChat(RadioChannels.System,"Система Vesper: свободный эфир.");
            await Until(()=>tabs.GetTabTitle(1).Contains("(1)"),3,"System message required.");tabs.CurrentTab=1;await Pause(.1);await Capture("02_system");
            tabs.CurrentTab=0;var reply=false;net.CommsCompleted+=r=>reply=r.Success;
            net.RequestComms(5000);await Until(()=>reply,3,"NPC reply required.");
            Check(history.GetParsedText().Contains("Dawn Trader"),"NPC replies must appear in Local.");
            var local=(System.Collections.IList)Field<System.Collections.IDictionary>(radio,"_history")[RadioChannels.Local]!;
            Check(local.Cast<object>().Any(row=>row.GetType().GetProperty("SenderId")!.GetValue(row) is uint sender && sender==5000),
                "History must retain NPC ids for future participant channels.");await Capture("03_local_npc");
            SetKey(Key.F1,true);SetKey(Key.F1,false);await Pause(.1);Check(radio.Visible,"Chat must remain in F1.");await Capture("04_tactical");
            SetKey(Key.F3,true);SetKey(Key.F3,false);await Pause(.1);await Capture("05_cockpit");
            SetKey(Key.F2,true);SetKey(Key.F2,false);await Pause(.5);
            foreach(var visible in new[]{false,true})
            {
                radio.Visible=visible;await Pause(.7);_frames.Clear();_measure=true;await Pause(5);_measure=false;
                var times=_frames.OrderBy(t=>t).ToArray();
                GD.Print($"RADIO FRAME TELEMETRY visible={visible}, frames={times.Length}, p95_ms={times[(int)(times.Length*.95)]:F3}, viewport={GetViewport().GetVisibleRect().Size}, scale={GetViewport().Scaling3DScale}, MSAA={GetViewport().Msaa3D}.");
            }
            SetKey(Key.Enter,true);SetKey(Key.Enter,false);await Pause(.1);SetKey(Key.Escape,true);SetKey(Key.Escape,false);await Pause(.1);
            Check(!radio.IsTyping && !menu.IsOpen,"Escape must leave chat without opening a flight menu.");
            SetKey(Key.R,true);await Pause(.7);SetKey(Key.R,false);
            Check(IndifferentStars.Shared.Vec3d.Distance(position,pilot.Track.WorldPosition)>5,"Flight must resume after typing.");
            tabs.CurrentTab=0;
            for(var i=0;i<110;i++)radio.Radio("History",$"Message {i}: retained scrollback");
            await Pause(.1);var scroll=history.GetVScrollBar();scroll.Value=0;
            radio.Radio("History","New message while reading older entries");await Pause(.1);
            Check(scroll.Value<1,"Incoming messages must preserve manual scrollback.");
            var histories=Field<System.Collections.IDictionary>(radio,"_history");
            Check(((System.Collections.IList)histories[RadioChannels.Local]!).Count==100,"History must stay bounded.");
            GD.Print("RADIO CHECKS PASSED: two clients, three channels, NPC history, unread tabs, plain text, deduplication, typing focus and all views.");
            _other.Dispose();_other=null;await game.QuitGracefully();
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture required.");
            }
        }
        catch(Exception error){GD.PrintErr(error);_other?.Dispose();if(game is not null)await game.QuitGracefully(1);else GetTree().Quit(1);}
    }
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void SetKey(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> condition,double seconds,string message){var end=Time.GetTicksMsec()+(ulong)(seconds*1000);while(!condition()&&Time.GetTicksMsec()<end)await Pause(.025);Check(condition(),message);}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
