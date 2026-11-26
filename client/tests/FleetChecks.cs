using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.World;

public sealed partial class FleetChecks : Node3D
{
    public override async void _Ready()
    {
        GameMain? game = null;
        try
        {
            var output = System.Environment.GetEnvironmentVariable("IS_FLEET_CAPTURE") ?? "/tmp/indifferent-fleet";
            Directory.CreateDirectory(output);
            if (System.Environment.GetEnvironmentVariable("IS_FLEET_PLAY") == "1")
            {
                game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
                AddChild(game);
                var menu = game.FindChildren("*", "CanvasLayer", true, false).OfType<SettingsMenu>().Single();
                menu.FindChildren("*", "Button", true, false).OfType<Button>()
                    .Single(button => button.Text == Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
                var expected = HullCatalog.Find(System.Environment.GetEnvironmentVariable("IS_FLEET_HULL") ?? "tern")!;
                var field = typeof(GameMain).GetField("_playerShip",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var limit = Time.GetTicksMsec() + 17000;
                ShipView? pilot = null;
                while (Time.GetTicksMsec() < limit)
                {
                    pilot = field.GetValue(game) as ShipView;
                    if (pilot?.Track.HasData ?? false) break;
                    await Pause(.05);
                }
                if (pilot is null || !pilot.Track.HasData || pilot.Hull.Id != expected.Id)
                    throw new InvalidOperationException("The diagnostic hull was not received from the server.");
                Tap(Key.F2);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = true });
                await Pause(1.2);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = false });
                if (pilot.Track.Velocity.Length() < 5) throw new InvalidOperationException("The diagnostic ship did not respond to flight input.");
                await Measure(expected.Key + "-flight");
                await Capture(output, expected.Key + "-flight");
                Tap(Key.F3);
                await Pause(.25);
                var camera = GetViewport().GetCamera3D();
                if (camera.GlobalPosition.DistanceTo(pilot.GlobalTransform * pilot.PilotEye) > .02)
                    throw new InvalidOperationException("The cockpit camera did not follow the hull's eye marker.");
                await Capture(output, expected.Key + "-cockpit");
                Tap(Key.F1);
                await Pause(.25);
                await Capture(output, expected.Key + "-tactical");
                GD.Print($"FLEET FLIGHT PASSED: {expected.Name}, F1/F2/F3, native input and cockpit eye.");
                await game.QuitGracefully();
                return;
            }

            AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color(.023f, .035f, .061f),
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(.65f, .76f, .96f),
                    AmbientLightEnergy = .34f,
                },
            });
            AddLight(new Vector3(-12, 15, -17), new Color(1, .88f, .70f), 3.1f);
            AddLight(new Vector3(14, 6, 5), new Color(.46f, .70f, 1), 2.6f);
            AddLight(new Vector3(-11, -12, 4), new Color(.67f, .80f, 1), .83f);
            var cameraNode = new Camera3D { Fov = 40, Current = true };
            AddChild(cameraNode);
            CollisionAssets.Validate();
            foreach (var hull in HullCatalog.All)
            {
                var view = new ShipView((uint)(10000 + hull.Id), true, hull.Name, false, hull.Id);
                AddChild(view);
                view.SetEngineEffects(false);
                view.SetParticles(false);
                var radius = (float)hull.Radius;
                cameraNode.LookAtFromPosition(new Vector3(-1.39f, 1.03f, -1.69f) * radius, Vector3.Zero, Vector3.Up);
                await Measure(hull.Key);
                await Capture(output, hull.Key + "-bow");
                cameraNode.LookAtFromPosition(new Vector3(1.50f, .82f, 1.62f) * radius, Vector3.Zero, Vector3.Up);
                await Capture(output, hull.Key + "-stern");
                RemoveChild(view);
                view.QueueFree();
            }
            GD.Print($"FLEET RENDERS PASSED: {HullCatalog.All.Count} hulls.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            if (game is not null) await game.QuitGracefully(1);
            else GetTree().Quit(1);
        }
    }

    private void AddLight(Vector3 position, Color color, float energy)
    {
        var lamp = new DirectionalLight3D { LightColor = color, LightEnergy = energy };
        AddChild(lamp);
        lamp.LookAtFromPosition(position, Vector3.Zero, Vector3.Up);
    }

    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Measure(string scenario)
    {
        var samples = new double[60];
        await Pause(.3);
        var last = Time.GetTicksUsec();
        for (var i = 0; i < samples.Length; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var now = Time.GetTicksUsec();
            samples[i] = (now - last) / 1000.0;
            last = now;
        }
        Array.Sort(samples);
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var memory = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024 * 1024);
        GD.Print(FormattableString.Invariant($"FLEET SAMPLE {scenario}: frames=60 p95_ms={samples[57]:F2} draws={draws:F0} vram_mib={memory:F1}"));
    }

    private async Task Capture(string output, string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(output, name + ".png")) != Error.Ok)
            throw new InvalidOperationException("Could not write the fleet capture.");
    }

    private static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }
}
