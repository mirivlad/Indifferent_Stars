using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

/// <summary>Real-time graphical smoke test against a separately started authoritative server.</summary>
public sealed partial class FlightChecks : Node
{
    private readonly List<double> _cruiseFrames = new();
    private bool _measuringCruise;
    private ulong _previousFrame;

    public override void _Process(double delta)
    {
        var now = Time.GetTicksUsec();
        if (_measuringCruise && _previousFrame != 0) _cruiseFrames.Add((now - _previousFrame) / 1000.0);
        _previousFrame = now;
    }

    public override async void _Ready()
    {
        try
        {
            System.Environment.SetEnvironmentVariable("IS_TRACE", "1");
            var output = System.Environment.GetEnvironmentVariable("IS_FLIGHT_CAPTURE") ?? Path.Combine(Path.GetTempPath(), "indifferent-flight-checks");
            Directory.CreateDirectory(output);
            var game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            ShipView? ship = null;
            for (var i = 0; i < 100; i++)
            {
                await Pause(0.1);
                ship = game.FindChildren("Ship_*", "Node3D", true, false).OfType<ShipView>().FirstOrDefault(view => view.IsPlayer && view.Track.HasData);
                if (ship is not null) break;
            }
            Check(ship is not null, "A real UDP server connection is required.");
            var pilot = ship!;
            var tactical = game.FindChildren("*", "Control", true, false).OfType<TacticalView>().First();
            var catalog = WorldCatalog.Generate(GameMain.DefaultSeed);
            Tap(Key.F1); await Pause(0.1);
            for (var i = 0; i < 50 && tactical.TargetId != catalog.Planet.Id; i++) { Tap(Key.Tab); await Pause(0.06); }
            Check(tactical.TargetId == catalog.Planet.Id, "Keyboard targeting must include the known planet.");
            tactical.ZoomAt(tactical.Size / 2, 6);
            await Pause(1);
            var waypoint = pilot.Track.WorldPosition + new Vec3d(-30_000, 0, 0);
            game._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right, Pressed = true, Position = tactical.ScreenPosition(waypoint),
            });
            var menu = game.FindChildren("*", "PopupMenu", true, false).Cast<PopupMenu>().First();
            Check(menu.GetItemIndex(9) >= 0, "Empty map space must offer a waypoint order.");
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, 9L);
            menu.Hide();
            await Pause(1);
            Check(pilot.Track.ApproachPoint is { } point && Vec3d.Distance(point, waypoint) < 0.1,
                "2D waypoint must reach the server and appear in the authoritative snapshot.");
            await Capture(output, "waypoint_tactical");
            Tap(Key.F2);
            var start = pilot.Track.WorldPosition;
            GD.Print("FLIGHT CHECK: leaving station at local boost.");
            SetKey(Key.Shift, true); SetKey(Key.W, true);
            await Pause(35);
            SetKey(Key.W, false); SetKey(Key.Shift, false);
            await Pause(0.3);
            Check(Vec3d.Distance(start, pilot.Track.WorldPosition) > 20_000, "Flight must pass the old sector boundary without returning.");
            Check(pilot.Track.Velocity.Length() > 700, "Releasing thrust must preserve inertia.");
            GD.Print("FLIGHT CHECK: cruise acceleration.");
            Tap(Key.V);
            _measuringCruise = true;
            await Pause(12);
            Check(pilot.Track.Cruising && pilot.Track.Velocity.Length() > 19_990, "Cruise must reach 20 km/s.");
            Check(game.GetNode<Camera3D>("ViewCamera").GlobalPosition.DistanceTo(pilot.GlobalPosition) < 25,
                "Chase camera must not fall kilometres behind at cruise speed.");
            await Capture(output, "cruise_external");
            Tap(Key.F3); await Pause(1);
            Check(pilot.Track.Cruising, "Cockpit switch must preserve cruise.");
            await Capture(output, "cruise_cockpit");
            Tap(Key.F1); await Pause(1);
            Check(pilot.Track.Cruising, "Tactical switch must preserve cruise.");
            await Capture(output, "cruise_tactical");
            _measuringCruise = false;
            for (var i = 0; i < 50 && tactical.TargetId != catalog.Planet.Id; i++) { Tap(Key.Tab); await Pause(0.06); }
            Check(tactical.TargetId == catalog.Planet.Id, "Keyboard targeting must include the known planet.");
            Tap(Key.X);
            await Pause(2);
            var navigation = SurfaceNavigation.Measure(catalog.LocalFrameOrigin + pilot.Track.WorldPosition, Vec3d.Zero, catalog.Planet);
            Check(Math.Abs(navigation.DistanceMetres - catalog.Planet.AtmosphereHeight - 10_000) < 1, "Warp must arrive 10 km above atmosphere.");
            Check(pilot.Track.Velocity.Length() < 0.1f && !pilot.Track.Cruising, "Warp must stop the ship and cancel cruise.");
            Tap(Key.Home); await Pause(1); await Capture(output, "arrival_tactical");
            Tap(Key.F2); await Pause(1); await Capture(output, "arrival_external");
            GD.Print("FLIGHT CHECK: maneuvering and active braking.");
            SetKey(Key.A, true); SetKey(Key.Q, true); await Pause(0.8);
            await Capture(output, "maneuver");
            SetKey(Key.A, false); SetKey(Key.Q, false);
            SetKey(Key.Space, true); await Pause(2); SetKey(Key.Space, false);
            Check(pilot.Track.Velocity.Length() < 0.1f, "Active engine braking must stop lateral motion.");
            var sorted = _cruiseFrames.Order().ToArray();
            GD.Print($"FLIGHT CHECK PASSED: distance={Vec3d.Distance(start, pilot.Track.WorldPosition) / 1000:F1} km; surface={navigation.DistanceMetres / 1000:F1} km; cruise frames={sorted.Length}; frame p95={sorted[(int)(sorted.Length * 0.95)]:F2} ms.");
            await game.QuitGracefully();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private async Task Capture(string directory, string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png")) == Error.Ok, "Capture must be saved.");
    }

    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Tap(Key key) { SetKey(key, true); SetKey(key, false); }
    private static void SetKey(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
