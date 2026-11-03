using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.Protocol;

public sealed partial class ImpactChecks : Node
{
    private NetworkClient? _observer;
    public override void _Process(double delta){_observer?.Poll();_observer?.Update(delta,IndifferentStars.Simulation.InputState.None);}
    public override async void _Ready()
    {
        GameMain? game=null;
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_IMPACT_CAPTURE")??"/tmp/indifferent-impacts";Directory.CreateDirectory(output);
            game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            ShipView? pilot=null;
            await Until(()=>{pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);return pilot is not null;},12,"Connection required.");
            var args=OS.GetCmdlineUserArgs();var endpoint=args[Array.IndexOf(args,"--server")+1].Split(':');
            _observer=new NetworkClient(endpoint[0],int.Parse(endpoint[1]),"Impact observer");
            ImpactState? observed=null;_observer.SnapshotReceived+=snapshot=>observed=snapshot.Entities.FirstOrDefault(e=>e.Id==pilot!.Id)?.Impact??observed;
            _observer.Start();await Until(()=>_observer.State==ConnectionState.Connected,5,"Second client required.");
            ImpactState? own=null;pilot!.ImpactOccurred+=hit=>own=hit;
            SetKey(Key.F2,true);SetKey(Key.F2,false);SetKey(Key.W,true);
            await Until(()=>own is not null,12,"Approach must produce an authoritative hull impact; "+
                $"initial position={pilot.Track.WorldPosition}, typing={game.GetNode<RadioChat>("RadioChat").IsTyping}.");SetKey(Key.W,false);
            await Until(()=>observed?.Sequence==own!.Sequence,2,"Observer must receive the same impact.");
            Check(observed==own,"Both clients must agree on point, normal and speed.");
            var effects=pilot.GetNode<ImpactEffects>("ImpactEffects");
            Check(effects.BurstCount>0,"Server impact must create sparks.");
            Check(effects.GetChildren().OfType<AudioStreamPlayer>().Any(a=>a.Playing),"Impact audio must play.");
            await Pause(.18);await Capture("01_external_impact");
            SetKey(Key.F3,true);SetKey(Key.F3,false);await Capture("02_cockpit_impact");
            SetKey(Key.F1,true);SetKey(Key.F1,false);await Capture("03_tactical_impact");
            SetKey(Key.F2,true);SetKey(Key.F2,false);
            var start=pilot.Track.WorldPosition;
            var normal=ShipTrack.ToVector3(pilot.Track.Impact?.Normal??own!.Normal);
            var until=Time.GetTicksMsec()+4000;
            while(IndifferentStars.Shared.Vec3d.Distance(start,pilot.Track.WorldPosition)<=10 && Time.GetTicksMsec()<until)
            {
                var local=pilot.GlobalBasis.Inverse()*normal;
                SetKey(Key.D,local.X>.2f);SetKey(Key.A,local.X<-.2f);
                SetKey(Key.R,local.Y>.2f);SetKey(Key.C,local.Y<-.2f);
                SetKey(Key.S,local.Z>.2f);SetKey(Key.W,local.Z<-.2f);
                await Pause(.05);
            }
            foreach(var key in new[]{Key.D,Key.A,Key.R,Key.C,Key.S,Key.W})SetKey(key,false);
            Check(IndifferentStars.Shared.Vec3d.Distance(start,pilot.Track.WorldPosition)>10,
                $"Outward thrust must depart: start={start}, end={pilot.Track.WorldPosition}, v={pilot.Track.Velocity}, n={normal}.");
            foreach(var kind in Enum.GetValues<ImpactSoundKind>())
            {
                var sample=ImpactSound.Sample(kind,2);
                Check(sample.GetLength()>.4 && sample.SaveToWav(Path.Combine(output,kind.ToString().ToLowerInvariant()+".wav"))==Error.Ok,"Original sound sample must export.");
            }
            GD.Print($"IMPACT CHECKS PASSED: two clients, same event, sparks, playing audio, F1/F2/F3 and departure; speed={own!.NormalSpeed:F2}, sliding={own.TangentialSpeed:F2}.");
            _observer.Dispose();_observer=null;await game.QuitGracefully();
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture required.");
            }
        }
        catch(Exception error){GD.PrintErr(error);_observer?.Dispose();if(game is not null)await game.QuitGracefully(1);else GetTree().Quit(1);}
    }
    private static void SetKey(Godot.Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> condition,double timeout,string message){var end=Time.GetTicksMsec()+(ulong)(timeout*1000);while(!condition()&&Time.GetTicksMsec()<end)await Pause(.025);Check(condition(),message);}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
