using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

public sealed partial class ScanRecoveryChecks : Node
{
    public override async void _Ready()
    {
        GameMain? game=null;
        try
        {
            game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            ShipView? pilot=null;
            await Until(()=> {pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);return pilot is not null;},10,"Connection required.");
            var net=Field<NetworkClient>(game,"_net");
            var hud=Field<Hud>(game,"_hud");
            var notices=new List<string>();hud.NoticeRaised+=notices.Add;
            ScanResultMessage? reply=null;var replies=0;net.ScanCompleted+=r=>{reply=r;replies++;};
            var catalog=WorldCatalog.Generate(GameMain.DefaultSeed);
            net.RequestScan((uint)catalog.Station.Id);
            await Until(()=>reply is not null,3,"Station scan response required.");Check(reply!.Success,"Nearby station scan must succeed.");
            SetKey(Key.R,true);await Pause(.7);SetKey(Key.R,false);
            Check(pilot!.Track.Velocity.Length()>10,"Successful scan must preserve flight input.");
            reply=null;net.RequestScan((uint)catalog.Planet.Id);
            await Until(()=>reply is not null,3,"Distant planet scan response required.");Check(!reply!.Success,"Planet scan must be rejected.");
            var height=pilot.Track.WorldPosition.Y;
            SetKey(Key.R,true);await Pause(.7);SetKey(Key.R,false);
            Check(pilot.Track.WorldPosition.Y>height+5,"Rejected scan must preserve movement.");
            var count=replies;
            Field<ConcurrentQueue<(IProtocolMessage,double)>>(net,"_incoming").Enqueue((reply!,Time.GetTicksUsec()/1000000.0));
            await Pause(.1);
            Check(replies==count,"Duplicate scan replies must not restart notifications.");
            await Pause(3.5);
            Check(notices.Any(n=>n.Contains(Locale.Text("scan_rejected"))),"Rejected scan must remain in notifications.");
            hud.SetScanPending("Lost response");
            typeof(NetworkClient).GetField("_pendingScan",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(net,(999u,0u,-10d,-10d));
            await Pause(.1);Check(notices.Any(n=>n.Contains(Locale.Text("scan_timeout"))),"Timeout must reach notification history.");Check(!net.IsScanPending,"Timed-out scan must release the pending request.");
            await Pause(3.5);Check(notices.Count(n=>n.Contains(Locale.Text("scan_timeout")))==1,"Timeout notification must not repeat.");
            GD.Print("SCAN RECOVERY CHECKS PASSED: success, rejection, continued input and bounded notification.");
            await game.QuitGracefully();
        }
        catch(Exception error){GD.PrintErr(error);if(game is not null)await game.QuitGracefully(1);else GetTree().Quit(1);}
    }
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void SetKey(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> predicate,double timeout,string message){var end=Time.GetTicksMsec()+(ulong)(timeout*1000);while(!predicate()&&Time.GetTicksMsec()<end)await Pause(.05);Check(predicate(),message);}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
