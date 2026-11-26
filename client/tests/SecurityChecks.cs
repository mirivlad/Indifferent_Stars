using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class SecurityChecks : Node
{
    private NetworkClient? _observer;
    private InputState _observerInput = InputState.None;

    public override void _Process(double delta)
    {
        _observer?.Poll();
        _observer?.Update(delta, _observerInput);
    }

    public override async void _Ready()
    {
        GameMain? game = null;
        var exitCode = 0;
        try
        {
            var endpoint = (System.Environment.GetEnvironmentVariable("IS_SERVER") ?? "127.0.0.1:17896").Split(':');
            var output = System.Environment.GetEnvironmentVariable("IS_SECURITY_CAPTURE") ?? "/tmp/indifferent-security";
            Directory.CreateDirectory(output);
            game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            var menu = game.FindChildren("*", "CanvasLayer", true, false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            await Until(() => Field<ShipView?>(game, "_playerShip")?.Track.HasData == true, 20, "Pilot connected");
            var pilot = Field<ShipView>(game, "_playerShip");
            var network = Field<NetworkClient>(game, "_net");
            var catalog = Field<WorldCatalog>(game, "_catalog");
            var hud = Field<Hud>(game, "_hud");
            var zone = Field<Label>(hud, "_securityZone");
            await Until(() => zone.Text == Locale.Text("security_trade"), 3, "Trade zone shown");
            var territory = catalog.RaiderTerritories[0];
            var destination = territory.Hideout + Vec3d.Up * 2000;

            var observed = new Dictionary<uint, SensorContact>();
            Vec3d observerPosition = Vec3d.Zero;
            uint observerId = 0;
            _observer = new NetworkClient(endpoint[0], int.Parse(endpoint[1]), "Security observer");
            _observer.WelcomeReceived += welcome => observerId = welcome.PlayerId;
            _observer.SnapshotReceived += snapshot =>
            {
                foreach (var entity in snapshot.Entities)
                    if (entity.Id == observerId) observerPosition = entity.Position;
            };
            _observer.SensorsReceived += report =>
            {
                foreach (var contact in report.Contacts) observed[contact.Id] = contact;
            };
            _observer.Start();
            await Until(() => _observer.State == ConnectionState.Connected, 10, "Independent observer connected");
            var contacts = Field<Dictionary<uint, SensorContact>>(game, "_sensorContacts");
            Check(!contacts.Values.Any(IsRaider), "Silent raiders beyond tactical range do not appear by faction");
            Hold(Key.R, true);
            _observerInput = InputState.None with { Vertical = 1, Strafe = -.14f };
            await Until(() => Vec3d.Distance(pilot.Track.WorldPosition, catalog.Station.Position) > 4800 &&
                Vec3d.Distance(observerPosition, catalog.Station.Position) > 4800, 35, "Both ships cleared the station under thrust");
            Hold(Key.R, false);
            _observerInput = InputState.None;
            var pilotWarp = false;
            var observerWarp = false;
            network.WarpCompleted += warp => pilotWarp = warp.Success;
            _observer.WarpCompleted += warp => observerWarp = warp.Success;
            network.RequestWarp(0, destination);
            _observer.RequestWarp(0, destination + new Vec3d(2900, 0, 1300));
            await Until(() => pilotWarp && observerWarp, 6, "Both warp orders accepted by the server");
            await Until(() => zone.Text == Locale.Text("security_pirate") && pilot.Track.WarpStage == WarpStage.None, 20, "Pirate territory shown after warp");
            await Until(() => contacts.Values.Any(contact => IsRaider(contact) && observed.TryGetValue(contact.Id, out var item) &&
                IsRaider(item)), 8, "Both pilots detected the same silent raider");
            var raider = contacts.Values.First(contact => IsRaider(contact) && observed.TryGetValue(contact.Id, out var item) && IsRaider(item));
            var map = Field<TacticalView>(game, "_tactical");
            Tap(Key.F1);
            map.ZoomAt(map.Size / 2, 70_000 / map.ZoomRange);
            await Until(() => map.Visible && map.Contacts.Any(contact => contact.Id == raider.Id && contact.Color.R > .9f &&
                contact.Color.G < .5f), 4, "Tactical contact appears hostile");
            await Pause(.5);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "pirate-territory.png")) == Error.Ok, "Security capture saved");
            GD.Print($"SECURITY CHECKS PASSED: protected departure, pirate territory after warp, same silent hostile raider {raider.Id} on both clients");
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

    private static bool IsRaider(SensorContact contact) => contact is
        { Faction: Faction.Pirate, Standing: FactionStanding.Hostile, Source: ContactSource.Tactical, Transponder: false, DestinationId: 0 };

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Tap(Key key)
    {
        Hold(key, true);
        Hold(key, false);
    }

    private static void Hold(Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

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
