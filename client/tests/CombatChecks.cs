using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Simulation;

public sealed partial class CombatChecks : Node
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
        var exitCode = 0;
        try
        {
            var output = System.Environment.GetEnvironmentVariable("IS_COMBAT_CAPTURE") ?? "/tmp/indifferent-combat";
            Directory.CreateDirectory(output);
            var endpoint = (System.Environment.GetEnvironmentVariable("IS_SERVER") ?? "127.0.0.1:17895").Split(':');
            game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            var menu = game.FindChildren("*", "CanvasLayer", true, false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            await Until(() => Field<ShipView?>(game, "_playerShip")?.Track.HasData == true, 18, "Player connection");
            var pilot = Field<ShipView>(game, "_playerShip");
            var net = Field<NetworkClient>(game, "_net");
            uint observerId = 0;
            CombatStatusMessage? observerState = null;
            var hits = 0;
            var attackerHits = 0;
            _observer = new NetworkClient(endpoint[0], int.Parse(endpoint[1]), "Combat observer");
            _observer.WelcomeReceived += welcome => observerId = welcome.PlayerId;
            _observer.CombatStatusReceived += status => observerState = status;
            _observer.CombatEventReceived += effect =>
            {
                if (effect.SourceId == pilot.Id && effect.TargetId == observerId && effect.Kind == CombatEventKind.ShieldHit)
                    hits++;
            };
            net.CombatEventReceived += effect =>
            {
                if (effect.SourceId == pilot.Id && effect.TargetId == observerId && effect.Kind == CombatEventKind.ShieldHit)
                    attackerHits++;
            };
            _observer.Start();
            var contacts = Field<Dictionary<uint, SensorContact>>(game, "_sensorContacts");
            await Until(() => observerId != 0 && contacts.ContainsKey(observerId), 10, "Observer in sensors");
            Tap(Key.F1);
            var map = Field<TacticalView>(game, "_tactical");
            await Until(() => map.Visible, 3, "F1 tactical view");
            map.ZoomAt(map.Size / 2, .38);
            await Pause(.45);
            var remoteShips = Field<Dictionary<uint, ShipView>>(game, "_remoteShips");
            await Until(() => remoteShips.TryGetValue(observerId, out var remote) && remote.Track.HasData, 3, "Rendered observer");
            var point = map.ScreenPosition(remoteShips[observerId].Track.WorldPosition);
            GetViewport().PushInput(new InputEventMouseButton
                { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            await Pause(.06);
            GetViewport().PushInput(new InputEventMouseButton
                { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
            if (Field<uint>(game, "_targetId") != observerId)
            {
                GD.Print($"COMBAT PICK: cursor={GetViewport().GetMousePosition()}, target={point}, viewport={map.Size}");
                await Capture(output, "target-selection");
            }
            await Until(() => Field<uint>(game, "_targetId") == observerId, 3, "Target selected in 2D");
            Tap(Key.B);
            await Until(() => Field<CombatStatusMessage?>(game, "_combatStatus")?.Order == CombatOrder.Attack, 3, "2D attack order");
            await Until(() => hits > 0 && attackerHits > 0 && observerState?.Shield < 1, 15, "Shared shot and authoritative shield loss");
            await Capture(output, "01-tactical");

            Tap(Key.F2);
            await Pause(.2);
            Check(!map.Visible && Field<CombatStatusMessage>(game, "_combatStatus").Order == CombatOrder.Attack, "F2 preserves attack");
            await Capture(output, "02-external");
            Tap(Key.F3);
            await Pause(.2);
            Check(GetViewport().GetCamera3D().GlobalPosition.DistanceTo(pilot.GlobalTransform * pilot.PilotEye) < .1f,
                "F3 follows the same cockpit");
            Check(Field<CombatStatusMessage>(game, "_combatStatus").Order == CombatOrder.Attack, "F3 preserves attack");
            await Capture(output, "03-cockpit");
            Tap(Key.B);
            await Until(() => Field<CombatStatusMessage>(game, "_combatStatus").Order == CombatOrder.None, 3, "Cockpit disengage");
            var before = attackerHits;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.J, PhysicalKeycode = Key.J, Pressed = true });
            await Until(() => attackerHits > before, 4, "Manual fire from the cockpit");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.J, PhysicalKeycode = Key.J, Pressed = false });
            Tap(Key.B);
            await Until(() => observerState?.Destroyed == true, 24, "Observer destruction");
            Tap(Key.F1);
            await Until(() => map.Visible && map.Wrecks.Length > 0, 4, "Wreck contact in 2D");
            await Capture(output, "04-wreck");
            await Until(() => observerState?.RecoveryIn == 0, 10, "Recovery countdown");
            var originalId = observerId;
            var recovered = false;
            _observer.CombatReplied += result =>
            {
                if (result.Command == CombatCommand.Recover) recovered = result.Accepted;
            };
            _observer.RequestCombat(CombatCommand.Recover);
            await Until(() => recovered && observerState?.Destroyed == false, 5, "Authoritative recovery");
            Check(observerId == originalId, "Recovery preserves the pilot ID");
            GD.Print($"COMBAT CHECKS PASSED: pilot {pilot.Id} in F1/F2/F3, target {observerId}, hits {hits}/{attackerHits}, destruction, wreck contact and recovery");
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            exitCode = 1;
        }
        _observer?.Dispose();
        _observer = null;
        if (game is not null) await game.QuitGracefully(exitCode);
        else GetTree().Quit(exitCode);
    }

    private async Task Capture(string folder, string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png")) == Error.Ok, "Capture failed");
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Until(Func<bool> condition, double seconds, string message)
    {
        var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!condition() && Time.GetTicksMsec() < end) await Pause(.05);
        Check(condition(), message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
