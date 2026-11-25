using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IndifferentStars.Server;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

internal static class NetworkDiagnostic
{
    public static async Task Run(string[] args)
    {
        var samples = args.Length > 1 ? Math.Clamp(int.Parse(args[1]), 40, 12000) : 1200;
        var clients = args.Length > 2 ? Math.Clamp(int.Parse(args[2]), 2, 64) : 4;
        var seed = args.Length > 3 ? int.Parse(args[3]) : 20261101;
        var system = args.Length > 4 ? int.Parse(args[4]) : 0;
        var stationApproach = args.Length > 5 && args[5] == "station-approach";
        var catalog = WorldCatalog.Generate(seed, system);
        var world = new GameWorld(catalog);
        using var server = new UdpGameServer(new ServerConfig
        {
            Port = 0, BindAddress = "127.0.0.1", MaxPlayers = clients,
        }, catalog, world);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(samples / 20.0 * 2 + 15));
        var times = new double[samples];
        var index = 0;
        var ready = 0;
        var clientTasks = Enumerable.Range(1, clients)
            .Select(i => DriveAsync(i, server.LocalEndpoint, stationApproach, () => Interlocked.Increment(ref ready), stop.Token))
            .ToArray();
        var serverTask = Task.Factory.StartNew(() => server.Run(stop.Token, elapsed =>
        {
            if (Volatile.Read(ref ready) == clients && index < samples)
                times[index++] = elapsed;
            if (index == samples) stop.Cancel();
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await Task.WhenAll(serverTask, Task.WhenAll(clientTasks));
        var states = await Task.WhenAll(clientTasks);
        if (index != samples || states.Select(s => s.PlayerId).Distinct().Count() != clients ||
            states.Any(s => s.PlayerId == 0 || s.OwnSnapshots < samples / 2 || !s.Moved))
            throw new InvalidOperationException("Incomplete UDP scenario: " + JsonSerializer.Serialize(states));

        Array.Sort(times);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            seed, system, clients, stationApproach, npcs = world.Npcs.Count, ticks = samples, tick_hz = 20,
            p50 = times[samples / 2], p95 = times[(int)(samples * .95)], p99 = times[(int)(samples * .99)],
            max = times[^1], over_50_ms = times.Count(t => t > 50), states,
        }));
    }

    private sealed record ClientState(uint PlayerId, int OwnSnapshots, bool Moved);

    private static async Task<ClientState> DriveAsync(int number, IPEndPoint endpoint, bool stationApproach,
        Action connected, CancellationToken stop)
    {
        using var socket = new UdpClient();
        socket.Connect(endpoint);
        var hello = ProtocolCodec.Encode(new HelloMessage(Guid.NewGuid(), $"Diagnostic {number}", SurfaceMesh.Fingerprint));
        uint id = 0, sequence = 0;
        ulong lastTick = 0;
        var snapshots = 0;
        var moved = false;
        var thrust = stationApproach ? 90 : -90;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                socket.Send(id == 0 ? hello : ProtocolCodec.Encode(new InputMessage(
                    ++sequence, (sbyte)(sequence % 100 < 40 ? thrust : 0), 0, 0, 0, false, sequence % 100 >= 70)));
                await Task.Delay(id == 0 ? 100 : 50, stop);
                while (socket.Available > 0 && !stop.IsCancellationRequested)
                {
                    var packet = await socket.ReceiveAsync(stop);
                    if (!ProtocolCodec.TryDecode(packet.Buffer, out var message, out _)) continue;
                    if (message is HelloAcceptedMessage welcome && id == 0)
                    {
                        id = welcome.PlayerId;
                        connected();
                    }
                    else if (message is SnapshotMessage snapshot && snapshot.Tick > lastTick &&
                        snapshot.Entities.FirstOrDefault(entity => entity.Id == id) is { } own)
                    {
                        lastTick = snapshot.Tick;
                        snapshots++;
                        moved |= own.Velocity.Length > 10;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        return new(id, snapshots, moved);
    }
}
