using System;
using System.Globalization;

namespace IndifferentStars.Server;

public sealed record ServerConfig
{
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 7777;
    public int TickRate { get; init; } = 20;
    public int SnapshotRate { get; init; } = 20;
    public int WorldSeed { get; init; } = 20261101;
    public int MaxPlayers { get; init; } = 8;

    public TimeSpan TickInterval => TimeSpan.FromSeconds(1.0 / TickRate);
    public TimeSpan SnapshotInterval => TimeSpan.FromSeconds(1.0 / SnapshotRate);

    public static ServerConfig Parse(string[] args)
    {
        var config = new ServerConfig
        {
            Port = ReadInt("IS_PORT", 7777),
            BindAddress = Environment.GetEnvironmentVariable("IS_BIND") ?? "0.0.0.0",
            WorldSeed = ReadInt("IS_SEED", 20261101),
        };

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (key)
            {
                case "--port" or "-p":
                    config = config with { Port = ParseInt(Next(), "--port", 1, 65535) };
                    break;
                case "--bind":
                    config = config with { BindAddress = Next() ?? config.BindAddress };
                    break;
                case "--tick-rate":
                    config = config with { TickRate = ParseInt(Next(), "--tick-rate", 1, 120) };
                    break;
                case "--snapshot-rate":
                    config = config with { SnapshotRate = ParseInt(Next(), "--snapshot-rate", 1, 120) };
                    break;
                case "--seed":
                    config = config with { WorldSeed = ParseInt(Next(), "--seed", int.MinValue, int.MaxValue) };
                    break;
                case "--max-players":
                    config = config with { MaxPlayers = ParseInt(Next(), "--max-players", 1, 64) };
                    break;
                case "--help" or "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {key}");
                    PrintHelp();
                    Environment.Exit(2);
                    break;
            }
        }

        return config;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            Indifferent Stars — dedicated headless server (M0 First Flight)

            Usage: dotnet run --project src/IndifferentStars.Server -- [options]

              --port <1-65535>        UDP port (default 7777, env IS_PORT)
              --bind <address>        Bind address (default 0.0.0.0, env IS_BIND)
              --tick-rate <1-120>     Simulation ticks per second (default 20)
              --snapshot-rate <1-120> Snapshots per second (default 20)
              --seed <int>            Star system seed (default 20261101, env IS_SEED)
              --max-players <1-64>    Player cap (default 8)
              --help                  Show this help
            """);
    }

    private static int ReadInt(string envName, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(envName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int ParseInt(string? text, string name, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            Console.Error.WriteLine($"Invalid value for {name}: {text ?? "<missing>"} (expected {min}..{max})");
            Environment.Exit(2);
        }

        return value;
    }
}