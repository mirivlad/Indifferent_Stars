using System;
using System.Threading;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Server;

public static class Program
{
    public static int Main(string[] args)
    {
        var config = ServerConfig.Parse(args);
        var catalog = WorldCatalog.Generate(config.WorldSeed);
        var world = new GameWorld(catalog);

        using var server = new UdpGameServer(config, catalog, world);
        using var shutdown = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
            server.Stop();
        };

        try
        {
            server.Run(shutdown.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal server error: {ex}");
            return 1;
        }

        return 0;
    }
}