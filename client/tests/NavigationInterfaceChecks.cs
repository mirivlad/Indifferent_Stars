using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

public sealed partial class NavigationInterfaceChecks : Node
{
    public override async void _Ready()
    {
        GameMain? game=null;
        try
        {
            var output=System.Environment.GetEnvironmentVariable("IS_NAVIGATION_CAPTURE")??"/tmp/indifferent-navigation";
            Directory.CreateDirectory(output);
            game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            var net=Field<NetworkClient>(game,"_net");
            ShipView? pilot=null;
            await Until(()=>{pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);return pilot is not null;},12,"Connected pilot required.");
            var navigation=Field<NavigationPanel>(game,"_navigation");
            var tree=navigation.FindChild("NavigationTree",true,false) as Tree??throw new Exception("Navigation tree missing.");
            var search=navigation.FindChild("NavigationSearch",true,false) as LineEdit??throw new Exception("Search missing.");
            var toggle=navigation.FindChild("NavigationToggle",true,false) as Button??throw new Exception("Accordion missing.");
            var catalog=WorldCatalog.Generate(GameMain.DefaultSeed);
            var radio=game.GetNode<RadioChat>("RadioChat");
            var tabs=(TabBar)radio.FindChild("RadioTabs",true,false);var log=(RichTextLabel)radio.FindChild("RadioHistory",true,false);
            await Pause(.5);
            var telemetry=Field<Label>(Field<Hud>(game,"_hud"),"_telemetryLabel").Text;
            Check(telemetry.Contains("ДО ПОВЕРХНОСТИ")&&!telemetry.Contains("ВЫСОТА"),"HUD must show nearest celestial surface clearance instead of frame Y.");
            Check(tree.GetRoot().GetChildCount()==6,"Six groups required.");
            var sortPanel=new NavigationPanel {Visible=false};AddChild(sortPanel);
            var sortTree=(Tree)sortPanel.FindChild("NavigationTree",true,false);
            var far=new NavigationContact(1,"Alpha","NPC",3000);
            var near=new NavigationContact(2,"Zulu","NPC",1000);
            sortPanel.UpdateContacts(new(){far,near},1);
            var ships=sortTree.GetRoot().GetChild(4);var originalNear=ships.GetFirstChild();
            Check(originalNear.GetMetadata(0).AsInt64()==2,"Navigation must order contacts by distance rather than name.");
            ships.Collapsed=true;
            sortPanel.UpdateContacts(new(){far with {Distance=500},near with {Distance=2000}},1);
            Check(ships.GetFirstChild().GetMetadata(0).AsInt64()==1&&ships.GetFirstChild().GetNext()==originalNear,
                "Changed distances must reorder existing rows without rebuilding the tree.");
            Check(ships.Collapsed&&sortTree.GetSelected().GetMetadata(0).AsInt64()==1,"Distance updates must retain collapsed groups and selected target.");
            var sortSearch=(LineEdit)sortPanel.FindChild("NavigationSearch",true,false);
            sortSearch.Text="Zulu";sortSearch.EmitSignal(LineEdit.SignalName.TextChanged,sortSearch.Text);
            Check(sortTree.GetRoot().GetChild(4).GetChildCount()==1,"Filtering must retain the distance-sorted contact.");
            sortPanel.QueueFree();
            toggle.ButtonPressed=false;await Pause(.1);Check(!tree.IsVisibleInTree(),"Navigation must collapse.");
            toggle.ButtonPressed=true;await Pause(.1);Check(tree.IsVisibleInTree(),"Navigation must expand.");
            Input.MouseMode=Input.MouseModeEnum.Captured;var target=Field<uint>(game,"_targetId");KeyPress(Key.Tab);await Pause(.1);
            Check(Input.MouseMode==Input.MouseModeEnum.Visible&&Field<uint>(game,"_targetId")==target,"Tab releases cursor and preserves target.");
            search.GrabFocus();search.Text=catalog.Station.Name;search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);await Pause(.2);
            var speed=pilot!.Track.Velocity.Length();SetKey(Key.W,true);await Pause(.3);SetKey(Key.W,false);
            Check(pilot.Track.Velocity.Length()<speed+.1,"Search typing must not steer ship.");
            var stationGroup=tree.GetRoot().GetChild(3);Check(stationGroup.GetChildCount()==1,"Type-ahead search must retain matching station.");
            Check(tree.GetRoot().GetChild(2).GetChildCount()==0,"Type-ahead search must filter asteroids.");
            KeyPress(Key.Tab);await Pause(.1);Check(!search.HasFocus()&&Input.MouseMode==Input.MouseModeEnum.Visible,"Tab leaves search safely.");
            stationGroup.GetFirstChild().Select(0);tree.EmitSignal(Tree.SignalName.ItemSelected);
            tree.EmitSignal(Tree.SignalName.ItemMouseSelected,Vector2.Zero,(long)MouseButton.Right);await Pause(.1);
            var context=Field<PopupMenu>(game,"_contactMenu");Check(context.Visible,"Right-click must open shared actions.");
            Check(context.GetItemIndex(20)>=0&&context.GetItemIndex(21)>=0&&context.GetItemIndex(22)>=0,"Information, lock and route actions required.");
            context.Hide();await Pause(.1);context.EmitSignal(PopupMenu.SignalName.IdPressed,20L);await Pause(.1);
            Check(navigation.IsModal,"Information must open modal.");
            var chatInput=(LineEdit)radio.FindChild("ChatInput",true,false);
            var at=chatInput.GlobalPosition+new Vector2(20,10);
            Input.ParseInputEvent(new InputEventMouseButton {Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=true});
            Input.ParseInputEvent(new InputEventMouseButton {Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=false});
            await Pause(.1);Check(!radio.IsTyping,"Modal must block clicks on the chat beneath it.");
            await Capture("01_information");
            await Pause(.1);
            ((Control)navigation.FindChild("ObjectInformation",true,false)).Hide();
            context.EmitSignal(PopupMenu.SignalName.IdPressed,22L);await Until(()=>!net.IsRoutePending,4,"Plan must be acknowledged.");
            Check(!pilot.Track.RouteActive,"Adding waypoint must not start autopilot.");
            Field<Control>(navigation,"_content").Visible=true;
            search.Text="";search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);await Pause(.3);
            // Add a second object through the same context menu used by the map.
            typeof(GameMain).GetField("_targetId",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(game,(uint)catalog.Star.Id);
            context.EmitSignal(PopupMenu.SignalName.IdPressed,22L);await Until(()=>!net.IsRoutePending,4,"Second waypoint must be acknowledged.");await Pause(.4);
            var route=Field<VBoxContainer>(navigation,"_route");
            Check(route.GetChildCount()==2,"Two route rows required.");
            ((Button)route.GetChild(1).GetChild(1)).EmitSignal(Button.SignalName.Pressed);
            await Until(()=>!net.IsRoutePending,4,"Reordered route must be acknowledged.");await Pause(.4);
            Check(((Label)route.GetChild(0).GetChild(0)).Text.Contains(catalog.Star.Name),"Reordering must change first destination.");
            ((Button)route.GetChild(0).GetChild(3)).EmitSignal(Button.SignalName.Pressed);
            await Until(()=>!net.IsRoutePending,4,"Removal must be acknowledged.");await Pause(.4);
            Check(route.GetChildCount()==1,"Waypoint must be removable.");
            tabs.CurrentTab=3;
            Field<Hud>(game,"_hud").ShowToast("Navigation notification check");await Pause(.1);
            Check(log.GetParsedText().Contains("Navigation notification check"),"System notices must enter Notifications.");
            Check(!((LineEdit)radio.FindChild("ChatInput",true,false)).Editable,"Notifications must be read-only.");
            foreach(var mode in new[]{Key.F2,Key.F1,Key.F3})
            {
                KeyPress(mode);await Pause(.3);Check(tree.IsVisibleInTree(),"Navigation must remain identical in every view.");
                await Capture(mode==Key.F1?"03_tactical":mode==Key.F2?"02_external":"04_cockpit");
            }
            KeyPress(Key.G);await Until(()=>pilot.Track.RouteActive,4,"G must explicitly start autopilot.");
            KeyPress(Key.G);await Until(()=>!pilot.Track.RouteActive,4,"G must stop autopilot.");
            Check(NavigationPanel.Distance(999).EndsWith(" м")&&NavigationPanel.Distance(1000).EndsWith(" км")&&NavigationPanel.Distance(1e6).Contains("тыс.")&&NavigationPanel.Distance(149597870700).Contains("а.е."),"Distance units must change at specified boundaries.");
            GD.Print("NAVIGATION INTERFACE CHECKS PASSED: accordion, search focus, Tab, shared menu, modal, route planning/reorder/remove, G, notifications and three views.");
            await game.QuitGracefully();
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture required.");
            }
        }
        catch(Exception error){GD.PrintErr(error);if(game is not null)await game.QuitGracefully(1);else GetTree().Quit(1);}
    }
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void KeyPress(Key key){SetKey(key,true);SetKey(key,false);}
    private static void SetKey(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Until(Func<bool> condition,double seconds,string message){var end=Time.GetTicksMsec()+(ulong)(seconds*1000);while(!condition()&&Time.GetTicksMsec()<end)await Pause(.025);Check(condition(),message);}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
