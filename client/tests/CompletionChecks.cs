using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;

public sealed partial class CompletionChecks : Node
{
    public override async void _Ready()
    {
        try
        {
            System.Environment.SetEnvironmentVariable("IS_TRACE", "1");
            var game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            ShipView? ship = null;
            for (var i = 0; i < 100; i++)
            {
                await Pause(0.1);
                ship = game.FindChildren("Ship_*", "Node3D", true, false).OfType<ShipView>()
                    .FirstOrDefault(view => view.IsPlayer && view.Track.HasData);
                if (ship is not null) break;
            }
            Check(ship is not null, "Connection required.");
            var pilot = ship!;
            var camera = game.GetNode<Camera3D>("ViewCamera");
            Tap(Key.F2);
            await Pause(0.5);
            var orientation = pilot.Track.Orientation;
            var cameraBefore = camera.GlobalPosition;
            SetKey(Key.Alt, true);
            await Pause(0.1);
            game._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(150, -70) });
            await Pause(0.6);
            Check(camera.GlobalPosition.DistanceTo(cameraBefore) > 5, "Alt must orbit the external camera.");
            Check(Mathf.Abs(pilot.Track.Orientation.Dot(orientation)) > 0.99999f, "Orbit must not steer the ship.");
            await Capture("external_orbit");
            SetKey(Key.Alt, false);
            await Pause(1);
            var released = camera.GlobalPosition;
            await Pause(0.5);
            Check(camera.GlobalPosition.DistanceTo(released) < 0.1f, "External orbit must stay after modifier release.");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            await Pause(0.1);
            game._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(-70, 0) });
            await Pause(0.5);
            Check(camera.GlobalPosition.DistanceTo(released) > 2, "Middle button must also orbit.");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
            Tap(Key.F3);
            await Pause(0.3);
            var cockpit = pilot.GetNode<CockpitView>("CockpitInterior");
            Check(cockpit.IsVisibleInTree(), "Cockpit must be visible.");
            Check(cockpit.FindChildren("Rivet*", "MeshInstance3D", true, false).Count >= 27, "Rails must have rivets.");
            await Capture("cockpit_thin_frame");
            var forward = camera.GlobalBasis;
            SetKey(Key.Alt, true);
            await Pause(0.1);
            game._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(60, 0) });
            await Pause(0.2);
            Check(!camera.GlobalBasis.IsEqualApprox(forward), "Cockpit modifier must turn the head.");
            var reticle = game.FindChildren("*", "Control", true, false).OfType<ReticleControl>().First();
            Check(reticle.NosePoint is { } nose && nose.DistanceTo(reticle.Size * 0.5f) > 30,
                $"The nose marker must remain separate: nose={reticle.NosePoint}, size={reticle.Size}, camera={camera.GlobalBasis}.");
            SetKey(Key.Alt, false);
            await Pause(0.2);
            Check(camera.GlobalBasis.IsEqualApprox(pilot.GlobalBasis), "Cockpit must return to forward view.");
            Check(Mathf.Abs(pilot.Track.Orientation.Dot(orientation)) > 0.99999f, "Looking around must preserve ship orientation.");
            GD.Print("COMPLETION CHECKS PASSED: Alt orbit, middle orbit, no ship steering, cockpit head look, forward reset, rivets.");
            await game.QuitGracefully();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private async Task Capture(string name)
    {
        var output = System.Environment.GetEnvironmentVariable("IS_COMPLETION_CAPTURE") ?? "/tmp/indifferent-completion";
        Directory.CreateDirectory(output);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "Capture required.");
    }

    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void SetKey(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    private static void Tap(Key key) { SetKey(key, true); SetKey(key, false); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
