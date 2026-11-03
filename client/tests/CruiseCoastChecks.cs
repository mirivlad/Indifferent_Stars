using System;
using System.Linq;
using System.Reflection;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

public sealed partial class CruiseCoastChecks : Node
{
    public override void _Ready()
    {
        try
        {
            var game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            game.SetProcess(false);
            game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single().Hide();
            var pilot=new ShipView(10000,true,"Coast check",true);game.AddChild(pilot);
            var velocity=new Vec3d(0,0,-20000);
            pilot.Track.Push(new EntitySnapshot(10000,EntityKind.Player,"Coast check",Vec3d.Zero,0,0,velocity,0),0,1);
            pilot.Track.Sample(0,Vec3d.Zero);
            Check(pilot.Track.Velocity.Length()>19999,"The input check requires a moving ship.");
            Set(game,"_playerShip",pilot);Set(game,"_flightAssist",false);
            var settings=(ClientSettings)Field(game,"_settings");
            foreach(var mode in Enum.GetValues<ViewMode>())
            {
                Set(game,"_mode",mode);Set(game,"_cruise",true);
                using var key=new InputEventKey {Keycode=settings.Keys["cruise"],Pressed=true};
                game._UnhandledInput(key);
                var input=(InputState)typeof(GameMain).GetMethod("ReadInput",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null)!;
                Check(!input.Cruise&&!input.Brake&&input.Throttle==0,$"Cruise off must release thrust without braking in {mode}.");
                var ship=new ShipState {Id=10000,Name="Coast",IsPlayer=true,Velocity=velocity};
                for(var i=0;i<40;i++)FlightModel.StepPlayer(ship,input with {Steer=1},.05);
                Check(Vec3d.Distance(ship.Velocity,velocity)<1e-8,"Turning with assist off must preserve the velocity vector at cruise speed.");
                Check(Vec3d.Dot(ship.Forward,velocity.Normalized())<0,"The ship must be able to turn past 90 degrees while coasting.");
                FlightModel.StepPlayer(ship,input with {Brake=true},.05);
                Check(ship.Velocity.Length<velocity.Length,"Explicit braking must still slow the ship.");
            }
            GD.Print("CRUISE COAST CHECKS PASSED: actual cruise key/input in F1/F2/F3, 20 km/s inertia, independent rotation and explicit brake.");
            GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr(error);GetTree().Quit(1);}
    }
    private static object Field(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,value);
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
