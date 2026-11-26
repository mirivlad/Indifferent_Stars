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

public sealed partial class EconomyChecks : Node
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
            var endpoint = (System.Environment.GetEnvironmentVariable("IS_SERVER") ?? "127.0.0.1:17897").Split(':');
            var output = System.Environment.GetEnvironmentVariable("IS_ECONOMY_CAPTURE") ?? "/tmp/indifferent-economy";
            Directory.CreateDirectory(output);
            game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            var menu = game.FindChildren("*", "CanvasLayer", true, false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            await Until(() => Field<ShipView?>(game, "_playerShip")?.Track.HasData == true, 20, "Main client connected");
            var station = Field<StationView>(game, "_stationView");
            var pilotNet = Field<NetworkClient>(game, "_net");

            MarketState? observed = null;
            _observer = new NetworkClient(endpoint[0], int.Parse(endpoint[1]), "Market observer");
            _observer.MarketReceived += market => observed = market.State;
            _observer.Start();
            await Until(() => _observer.State == ConnectionState.Connected, 10, "Independent market client connected");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = false });
            await Until(() => station.Visible && station.HasMarket, 60, "Docked with server market open");
            var market = Field<MarketState>(station, "_market");
            var water = Entry(market, CommodityCatalog.Water);
            Check(market.Entries.Length == CommodityCatalog.All.Count && market.Entries.Any(item => item.CommodityId == CommodityCatalog.Metal)
                && market.Entries.Any(item => item.CommodityId == CommodityCatalog.Equipment), "Eight commodity rows");
            Check(water.Capacity > water.StationStock && water.Demand > 0, "Server shows warehouse capacity and remaining demand");
            var buy = (Button?)station.FindChild($"MarketBuy_{CommodityCatalog.Water}", true, false);
            Check(buy is { Disabled: false }, "Native purchase button enabled");
            buy!.EmitSignal(Button.SignalName.Pressed);
            await Until(() => Entry(Field<MarketState>(station, "_market"), CommodityCatalog.Water).PlayerQuantity == 1, 6,
                "Native market button transfers cargo");
            var price = Entry(Field<MarketState>(station, "_market"), CommodityCatalog.Water).BuyPrice;
            TradeResultMessage? result = null;
            pilotNet.TradeCompleted += response => result = response;
            pilotNet.RequestTrade(CommodityCatalog.Water, 29, true, price);
            await Until(() => result?.Success == true, 6, "Bulk trade settled");
            var after = Entry(result!.State, CommodityCatalog.Water);
            Check(after.PlayerQuantity == 30 && after.BuyPrice > price, "Depletion raised the actual quote");
            _observer.RequestMarket();
            await Until(() => observed is not null && Entry(observed, CommodityCatalog.Water).BuyPrice >= after.BuyPrice, 6,
                "Second client sees the new market price");
            Check(Entry(observed!, CommodityCatalog.Water).PlayerQuantity == 0, "Market price is shared and cargo is private");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "station-market.png")) == Error.Ok, "Market capture saved");
            GD.Print($"ECONOMY CHECKS PASSED: two clients, trade button, warehouse capacity and demand, water price {price} -> {after.BuyPrice}");
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

    private static MarketEntry Entry(MarketState market, ushort commodity) =>
        market.Entries.Single(item => item.CommodityId == commodity);

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
