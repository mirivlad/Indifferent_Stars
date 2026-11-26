using System.Net.Sockets;
using IndifferentStars.Server;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed partial class ServerIntegrationTests
{
    [Fact]
    public async Task AttackAndDisengageAreAuthoritativeAcrossTwoUdpClients()
    {
        var catalog = WorldCatalog.Generate(20261101);
        using var server = new UdpGameServer(new ServerConfig { BindAddress = "127.0.0.1", Port = 0 },
            catalog, new GameWorld(catalog));
        using var stop = new CancellationTokenSource();
        var run = Task.Factory.StartNew(() => server.Run(stop.Token), stop.Token,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            using var attacker = new UdpClient(AddressFamily.InterNetwork);
            using var target = new UdpClient(AddressFamily.InterNetwork);
            attacker.Connect(server.LocalEndpoint);
            target.Connect(server.LocalEndpoint);
            Send(attacker, new HelloMessage(Guid.NewGuid(), "Attacker", SurfaceMesh.Fingerprint));
            var pilot = Assert.IsType<HelloAcceptedMessage>(WaitFor<HelloAcceptedMessage>(attacker, _ => true));
            Send(target, new HelloMessage(Guid.NewGuid(), "Target", SurfaceMesh.Fingerprint));
            var victim = Assert.IsType<HelloAcceptedMessage>(WaitFor<HelloAcceptedMessage>(target, _ => true));
            uint ping = 0;
            void Heartbeat()
            {
                Send(attacker, new PingMessage(++ping));
                Send(target, new PingMessage(ping));
            }

            var attack = new CombatCommandMessage(1, CombatCommand.Attack, victim.PlayerId);
            Send(attacker, attack);
            var accepted = Assert.IsType<CombatReplyMessage>(WaitFor<CombatReplyMessage>(attacker, m => m.RequestId == 1));
            Assert.True(accepted.Accepted);
            Assert.NotNull(WaitFor<CombatEventMessage>(target, m => m.Kind == CombatEventKind.ShieldHit &&
                m.SourceId == pilot.PlayerId && m.TargetId == victim.PlayerId, 12, Heartbeat));
            Assert.NotNull(WaitFor<CombatStatusMessage>(target, m => m.Shield < 1, 4, Heartbeat));
            Assert.NotNull(WaitFor<SensorContactsMessage>(attacker, report => report.Contacts.Any(contact =>
                contact.Faction == Faction.Security && contact.Standing == FactionStanding.Hostile), 5, Heartbeat));
            Assert.NotNull(WaitFor<SensorContactsMessage>(target, report => report.Contacts.Any(contact =>
                contact.Faction == Faction.Security && contact.Standing == FactionStanding.Neutral), 5, Heartbeat));
            var disengage = new CombatCommandMessage(2, CombatCommand.Stop);
            Send(attacker, disengage);
            var stopped = Assert.IsType<CombatReplyMessage>(WaitFor<CombatReplyMessage>(attacker, m => m.RequestId == 2));
            Assert.True(stopped.Accepted);
            Send(attacker, disengage);
            Assert.Equal(stopped, WaitFor<CombatReplyMessage>(attacker, m => m.RequestId == 2));
            Send(attacker, attack);
            Assert.False(WaitFor<CombatReplyMessage>(attacker, m => m.RequestId == 1)?.Accepted);
            Assert.NotNull(WaitFor<CombatStatusMessage>(attacker, m => m.Order == CombatOrder.None));
        }
        finally
        {
            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
