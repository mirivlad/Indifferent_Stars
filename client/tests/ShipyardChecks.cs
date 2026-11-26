using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class ShipyardChecks : Node
{
    private NetworkClient? _observer;

    public override void _Process(double delta)
    {
        _observer?.Poll();
        _observer?.Update(delta, InputState.None);
    }

    public override async void _Ready()
    {
        GameMain? game = null;
        var exit = 0;
        try
        {
            var endpoint = (System.Environment.GetEnvironmentVariable("IS_SERVER") ?? "127.0.0.1:17898").Split(':');
            var output = System.Environment.GetEnvironmentVariable("IS_SHIPYARD_CAPTURE") ?? "/tmp/indifferent-shipyard";
            Directory.CreateDirectory(output);
            game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            var menu = game.FindChildren("*", "CanvasLayer", true, false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            await Until(() => Field<ShipView?>(game, "_playerShip")?.Track.HasData == true, 20, "Pilot connected");
            var pilot = Field<ShipView>(game, "_playerShip");
            var station = Field<StationView>(game, "_stationView");
            var otherObserved = false;
            _observer = new NetworkClient(endpoint[0], int.Parse(endpoint[1]), "Shipyard observer");
            _observer.SnapshotReceived += state =>
            {
                if (state.Entities.Any(ship => ship.Id == pilot.Id && ship.HullId == 10 && ship.Docked)) otherObserved = true;
            };
            _observer.Start();
            await Until(() => _observer.State == ConnectionState.Connected, 10, "Observer connected");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = false });
            await Until(() => station.Visible && station.HasMarket, 60, "Pilot docked");
            var tabs = (TabBar)station.FindChild("StationTabs", true, false)!;
            tabs.CurrentTab = 1;
            await Until(() => Field<ShipyardState?>(station, "_yard") is not null, 6, "Shipyard offers opened");
            var list = (ItemList)station.FindChild("YardInventory", true, false)!;
            var sections = (TabBar)station.FindChild("YardSections", true, false)!;
            var primary = (Button)station.FindChild("YardPrimary", true, false)!;
            Select(Enumerable.Range(0, list.ItemCount).First(index => list.GetItemText(index).Contains("Pipit", StringComparison.Ordinal)));
            await Capture("01-hull-comparison");
            Press(primary);
            await Until(() => Field<ShipyardState>(station, "_yard").Vessels.Length == 2, 7, "Pipit purchased");
            Select(1);
            Press(primary);
            await Until(() => Field<ShipView>(game, "_playerShip").Hull.Id == 10 && otherObserved, 10,
                "Both clients render the purchased hull with the same pilot ID");

            sections.CurrentTab = 1;
            Select(Enumerable.Range(0, list.ItemCount).First(index => list.GetItemText(index)
                .Contains(Locale.Text("equipment_deep_sensor"), StringComparison.Ordinal)));
            Press(primary);
            await Until(() => Field<ShipyardState>(station, "_yard").Modules.Any(module => module.ModuleId == 4), 6,
                "Sensor in station storage");
            sections.CurrentTab = 2;
            Select(2);
            Press(primary);
            await Until(() => Field<ShipyardState>(station, "_yard").Vessels.Single(vessel => vessel.HullId == 10)
                .Modules.Any(module => module.ModuleId == 4), 6, "Sensor installed");
            Check(Field<float>(game, "_scanRange") >= HullCatalog.Get(10).ScanRange * 1.5 - 1, "Sensor range changed");
            await Capture("02-fitted-module");
            Check(Field<ShipyardState>(station, "_yard").Vessels.Any(vessel => vessel.HullId == HullCatalog.Courier),
                "Wren remains in station storage");
            GD.Print($"SHIPYARD CHECKS PASSED: bought and switched to Pipit, both clients saw pilot {pilot.Id}, installed a sensor and retained Wren");

            void Select(int index)
            {
                list.Select(index);
                list.EmitSignal(ItemList.SignalName.ItemSelected, index);
            }

            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "Shipyard capture saved");
            }
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            exit = 1;
        }
        _observer?.Dispose();
        _observer = null;
        if (game is not null) await game.QuitGracefully(exit);
        else GetTree().Quit(exit);
    }

    private static void Press(Button button)
    {
        Check(button.Visible && !button.Disabled, "Shipyard action available");
        button.EmitSignal(Button.SignalName.Pressed);
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private async Task Until(Func<bool> condition, double seconds, string message)
    {
        var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!condition() && Time.GetTicksMsec() < end)
            await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
        Check(condition(), message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
