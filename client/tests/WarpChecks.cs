using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

public sealed partial class WarpChecks : Node
{
    private GameMain _game = null!;
    private ShipView _pilot = null!;
    private string _output = "";
    private readonly List<double> _frames = new();
    private ulong _previousFrame;

    public override void _Process(double delta)
    {
        var now = Time.GetTicksUsec();
        if (_pilot is not null && _pilot.Track.WarpStage == WarpStage.Travel)
        {
            if (_previousFrame != 0) _frames.Add((now - _previousFrame) / 1000.0);
            _previousFrame = now;
        }
        else _previousFrame = 0;
    }

    public override async void _Ready()
    {
        try
        {
            System.Environment.SetEnvironmentVariable("IS_TRACE", "1");
            _output = System.Environment.GetEnvironmentVariable("IS_WARP_CAPTURE") ?? "/tmp/indifferent-warp";
            Directory.CreateDirectory(_output);
            _game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(_game);
            await Until(() =>
            {
                _pilot = _game.FindChildren("Ship_*", "Node3D", true, false).OfType<ShipView>()
                    .FirstOrDefault(view => view.IsPlayer && view.Track.HasData)!;
                return _pilot is not null;
            }, 12, "Server connection required.");
            Tap(Key.F2);
            SetKey(Key.R, true);
            await Until(() => _pilot.Track.WorldPosition.Y > ModelPose.StationDockingOffset.Y + 1500,
                20, "Departure must clear the enlarged station by normal thrust.");
            SetKey(Key.R, false);
            SetKey(Key.Space, true);
            await Pause(1.4);
            SetKey(Key.Space, false);
            Check(_pilot.Track.WorldPosition.Y > ModelPose.StationDockingOffset.Y + 1500,
                "Departure must clear nearby surfaces.");
            Tap(Key.F1);
            var map = _game.FindChildren("*", "Control", true, false).OfType<TacticalView>().First();
            map.ZoomAt(map.Size * 0.5f, 3);
            await Pause(0.8);
            var waypoint = new Vec3d(15000, _pilot.Track.WorldPosition.Y, 1000);
            _game._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right, Pressed = true, Position = map.ScreenPosition(waypoint),
            });
            var menu = _game.FindChildren("*", "PopupMenu", true, false).Cast<PopupMenu>().First();
            Check(Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemId(i) == 14), "Empty map point must offer unified warp.");
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, 14L); menu.Hide();
            await Until(() => _pilot.Track.WarpStage == WarpStage.Charging, 3, "Map warp must reach the server without cruise.");
            Check(_pilot.Track.WarpDestination is { } destination && Vec3d.Distance(destination, waypoint) < 0.1,
                "The server must publish the chosen point.");
            Tap(Key.F2);
            await Pause(0.8);
            await Capture("01_warp_charge");
            await Until(() => _pilot.Track.WarpStage == WarpStage.Travel && _pilot.Track.WarpProgress > 0.2, 4, "Travel must follow charging.");
            Check(_game.GetNode<Camera3D>("ViewCamera").Fov < 110, "Warp FOV must remain bounded.");
            await Capture("02_warp_external");
            Tap(Key.F3);
            await Pause(0.5);
            Check(_pilot.Track.WarpStage == WarpStage.Travel, "Cockpit switch must preserve warp.");
            await Capture("03_warp_cockpit");
            Tap(Key.F1);
            await Pause(0.4);
            Check(map.ApproachPoint is { } route && Vec3d.Distance(route, waypoint) < 0.1, "2D must retain the server warp route.");
            await Capture("04_warp_tactical");
            Tap(Key.F2);
            await Until(() => _pilot.Track.WarpStage == WarpStage.Exit, 4, "Warp must have an exit phase.");
            await Pause(0.2);
            await Capture("05_warp_exit");
            await Until(() => _pilot.Track.WarpStage == WarpStage.None, 3, "Warp must finish.");
            Check(Vec3d.Distance(_pilot.Track.WorldPosition, waypoint) < 1, "Map warp must arrive at the chosen point.");
            Check(_pilot.Track.Velocity.Length() < 0.1, "Arrival must stop the ship.");
            // A second jump requires neither a cooldown nor a cruise speed.
            Tap(Key.F1);
            var catalog = WorldCatalog.Generate(GameMain.DefaultSeed);
            for (var i = 0; i < 60 && map.TargetId != catalog.Planet.Id; i++) { Tap(Key.Tab); await Pause(0.04); }
            Check(map.TargetId == catalog.Planet.Id, "Known planet must be selectable.");
            Tap(Key.X);
            await Until(() => _pilot.Track.WarpStage == WarpStage.Travel, 4, "Planet warp needs no cruise or local range.");
            Tap(Key.F2);
            await Pause(0.3);
            await Capture("06_planet_transit");
            await Until(() => _pilot.Track.WarpStage == WarpStage.None, 7, "Planet warp must finish.");
            var navigation = SurfaceNavigation.Measure(catalog.LocalFrameOrigin + _pilot.Track.WorldPosition, Vec3d.Zero, catalog.Planet);
            Check(Math.Abs(navigation.DistanceMetres - catalog.Planet.AtmosphereHeight - WarpFlight.Clearance - SurfaceMesh.Ship.Radius) < 1,
                "Planet arrival must keep 1 km clearance for the whole hull.");
            await Pause(0.5);
            await Capture("07_planet_arrival");
            Tap(Key.F1);
            for (var i = 0; i < 60 && map.TargetId != catalog.Star.Id; i++) { Tap(Key.Tab); await Pause(0.04); }
            Check(map.TargetId == catalog.Star.Id, "Known star must be selectable.");
            Tap(Key.X);
            await Until(() => _pilot.Track.WarpStage == WarpStage.Travel && _pilot.Track.WarpProgress > 0.1, 5, "AU warp must start.");
            Tap(Key.F2);
            for (var i = 0; i < 20; i++)
            {
                await Pause(0.05);
                Check(_pilot.GlobalPosition.Length() < 15000, "AU warp must rebase around the rendered ship.");
                Check(_game.GetNode<Camera3D>("ViewCamera").GlobalPosition.DistanceTo(_pilot.GlobalPosition) < 25,
                    "The camera must stay with the AU-scale warp ship.");
            }
            await Capture("08_au_transit");
            await Until(() => _pilot.Track.WarpStage == WarpStage.None, 7, "AU warp must finish.");
            var starDistance = Vec3d.Distance(catalog.LocalFrameOrigin + _pilot.Track.WorldPosition, catalog.Star.SystemPosition) - catalog.Star.Radius;
            Check(Math.Abs(starDistance - WarpFlight.Clearance - SurfaceMesh.Ship.Radius) < 1, "Star arrival must preserve hull clearance.");
            await Capture("09_star_arrival");
            var sorted = _frames.Order().ToArray();
            Check(sorted.Length > 0, "Warp frame telemetry required.");
            GD.Print($"WARP FRAME TELEMETRY: frames={sorted.Length}, p95={sorted[(int)((sorted.Length - 1) * 0.95)]:F2} ms, viewport={GetViewport().GetVisibleRect().Size}, scale={GetViewport().Scaling3DScale}, MSAA={GetViewport().Msaa3D}.");
            GD.Print("WARP CHECKS PASSED: free map point, charging/travel/exit, all views, authoritative route, no cooldown/cruise/range, 1 km arrival, bounded FOV, AU-scale origin and camera.");
            await _game.QuitGracefully();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private async Task Until(Func<bool> predicate, double timeout, string message)
    {
        var until = Time.GetTicksMsec() + (ulong)(timeout * 1000);
        while (!predicate() && Time.GetTicksMsec() < until) await Pause(0.05);
        Check(predicate(), message);
    }

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, name + ".png")) == Error.Ok, "Capture required.");
    }

    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void SetKey(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    private static void Tap(Key key) { SetKey(key, true); SetKey(key, false); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
