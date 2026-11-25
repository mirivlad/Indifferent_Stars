using System;
using System.Linq;
using Godot;
using IndifferentStars.Client;

public sealed partial class PersistenceChecks : Node
{
    public override async void _Ready()
    {
        GameMain? game = null;
        try
        {
            game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();
            AddChild(game);
            var menu = game.FindChildren("*", "CanvasLayer", true, false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(button => button.Text == Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            var field = typeof(GameMain).GetField("_net", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var network = (NetworkClient)field!.GetValue(game)!;
            var end = Time.GetTicksMsec() + 15000;
            while (network.Welcome is null && network.State != ConnectionState.Rejected && Time.GetTicksMsec() < end)
                await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            var welcome = network.Welcome ?? throw new InvalidOperationException(network.StatusText);
            if (uint.TryParse(System.Environment.GetEnvironmentVariable("IS_EXPECT_PLAYER"), out var id) && id != welcome.PlayerId)
                throw new InvalidOperationException($"Expected player {id}, received {welcome.PlayerId}.");
            GD.Print($"PERSISTENCE CHECK PASSED: player={welcome.PlayerId} tick={welcome.ServerTick} name={welcome.PlayerName}");
            await game.QuitGracefully();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            if (game is not null) await game.QuitGracefully(1);
            else GetTree().Quit(1);
        }
    }
}
