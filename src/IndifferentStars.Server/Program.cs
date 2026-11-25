using System;
using System.Threading;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Server;

public static class Program
{
    public static int Main(string[] args)
    {
        ServerConfig? config = null;
        try
        {
            config = ServerConfig.Parse(args);
            var catalog = WorldCatalog.Generate(config.WorldSeed, config.SystemIndex);
            var world = new GameWorld(catalog);
            using var server = new UdpGameServer(config, catalog, world);
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });
            server.Run(shutdown.Token);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Console.Error.WriteLine($"UDP port {config!.Port} on {config.BindAddress} is already in use. Stop the existing server, connect to it, or choose another port with --port / IS_PORT.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal server error: {ex}");
            return 1;
        }

        return 0;
    }
}
