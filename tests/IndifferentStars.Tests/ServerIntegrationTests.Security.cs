using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IndifferentStars.Server;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed partial class ServerIntegrationTests
{
    [Fact]
    public void SecurityWorldSavesBudgetsAndReadsPreRaiderFormat()
    {
        var directory = Directory.CreateTempSubdirectory("indifferent-security-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.save");
            var catalog = WorldCatalog.Generate(20261101);
            var config = new ServerConfig { BindAddress = "127.0.0.1", Port = 0, SavePath = path };
            var world = new GameWorld(catalog);
            var state = world.CaptureSimulation();
            var budget = state.Security!;
            var region = catalog.RaiderTerritories[0];
            var economy = state.Economy!;
            economy.Stations[region.StationId].Credits -= 175;
            economy.Stations[region.StationId].Consumed[CommodityCatalog.Fuel] = 12;
            budget.PirateSupplies[region.StationId] -= 18;
            budget.ReplacementOrders[-region.StationId] = 185;
            var offense = new SecurityOffense(10002, 5002, new Vec3d(8, 10, 12), 135);
            world = new GameWorld(catalog);
            world.RestoreSimulation(state with
            {
                Security = budget with { Offenses = new[] { offense } },
                Economy = economy with { NextProduction = 14.25 },
            });
            using var stopped = new CancellationTokenSource();
            stopped.Cancel();
            using (var server = new UdpGameServer(config, catalog, world))
                server.Run(stopped.Token);

            var restart = new GameWorld(catalog);
            using (var server = new UdpGameServer(config, catalog, restart))
            {
                var loaded = restart.CaptureSimulation().Security!;
                Assert.Equal(54, loaded.PirateSupplies[region.StationId]);
                Assert.Equal(185, loaded.ReplacementOrders[-region.StationId]);
                Assert.Equal(offense, Assert.Single(loaded.Offenses));
                var restoredEconomy = restart.CaptureSimulation().Economy!;
                Assert.Equal(14.25, restoredEconomy.NextProduction);
                Assert.Equal(12, restoredEconomy.Stations[region.StationId].Consumed[CommodityCatalog.Fuel]);
                Assert.Equal(EconomyRules.InitialTreasury - 175, restoredEconomy.Stations[region.StationId].Credits);
            }

            var saved = File.ReadAllBytes(path);
            var document = JsonNode.Parse(saved.AsSpan(65))!.AsObject();
            document["FormatVersion"] = 3;
            var simulation = document["Simulation"]!.AsObject();
            simulation.Remove("Security");
            simulation.Remove("Economy");
            foreach (var stock in simulation["StationStocks"]!.AsObject())
            {
                stock.Value!.AsObject().Remove(CommodityCatalog.Metal.ToString());
                stock.Value.AsObject().Remove(CommodityCatalog.Equipment.ToString());
            }
            var npcs = simulation["Npcs"]!.AsArray();
            for (var i = npcs.Count - 1; i >= 0; i--)
            {
                var row = npcs[i]!.AsObject();
                if (row["Id"]!.GetValue<uint>() >= 9000) npcs.RemoveAt(i);
                else row["Work"]!.AsObject().Remove("Security");
            }
            var payload = Encoding.UTF8.GetBytes(document.ToJsonString());
            var signature = Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(payload)) + "\n");
            File.WriteAllBytes(path, signature.Concat(payload).ToArray());
            var migrated = new GameWorld(catalog);
            using (var server = new UdpGameServer(config, catalog, migrated))
            {
                Assert.Equal(state.Npcs.Select(ship => ship.Id), migrated.Npcs.Select(ship => ship.Id));
                Assert.Equal(72, migrated.CaptureSimulation().Security!.PirateSupplies[region.StationId]);
                Assert.Equal(2 * catalog.RaiderTerritories.Length, migrated.Npcs.Count(ship => ship.Work!.Occupation == NpcOccupation.Raider));
                server.Run(stopped.Token);
            }
            var upgraded = JsonNode.Parse(File.ReadAllBytes(path).AsSpan(65))!.AsObject();
            Assert.Equal(6, upgraded["FormatVersion"]!.GetValue<int>());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
