using System;
using IndifferentStars.Shared.Collision;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using Xunit;

namespace IndifferentStars.Tests;

public sealed class ProtocolTests
{
    public static TheoryData<IProtocolMessage> Messages()
    {
        var data = new TheoryData<IProtocolMessage>
        {
            new HelloMessage(Guid.Parse("b5c6d7e8-1111-2222-3333-444455556666"), "Captain Vega", SurfaceMesh.Fingerprint),
            new HelloAcceptedMessage(
                7, "Captain Vega", new Vec3d(10, 20, -30), 1.2f, 20261101,
                "Vesper — Kestrel Reach", 4200, 21000, 720, 42, 20),
            new HelloRejectedMessage("Protocol mismatch: client v0, server v1."),
            new InputMessage(1234, 127, -127, 64, -32, true, false, 50, -60, true, true),
            new SnapshotMessage(100, 5000, new[]
            {
                new EntitySnapshot(7, EntityKind.Player, "Captain Vega", new Vec3d(1.5, 2.5, 3.5), 0.5f, -0.2f, new Vec3d(10, 0, -4), 127, System.Numerics.Quaternion.CreateFromYawPitchRoll(0.5f, -0.2f, 0.7f), 5000, false, false, true, true, true, new Vec3d(30_000, 260, 0), true, true, WarpStage.Travel, 0.3f, new Vec3d(1000000, 100000, 0), new ImpactState(17,new Vec3d(1,2,3),Vec3d.Up,120,34)),
                new EntitySnapshot(5000, EntityKind.Npc, "Dawn Trader", new Vec3d(-400, 80, 1200), 3.1f, 0.1f, new Vec3d(-40, 0, -70), 90),
            }),
            new CommsRequestMessage(3),
            new CommsResultMessage(3, true, "Traffic control online.", "Диспетчер на связи."),
            new ScanRequestMessage(9, 1001),
            new ScanResultMessage(9, 1001, true, "Asteroid KV-1001", "Composition: silicate 40%", "Астероид", "Состав: силикаты 40%"),
            new WarpRequestMessage(10, 2),
            new WarpRequestMessage(11, 0, new Vec3d(1000000, 500, 2000000)),
            new WarpResultMessage(10, 2, true, "Short warp complete", new Vec3d(11000, -1000, -7000), 0.2f, -0.1f, "Варп завершён"),
            new PingMessage(987654321),
            new PongMessage(987654321),
            new DisconnectMessage("leaving"),
            new ApproachRequestMessage(1001, true),
            new ApproachRequestMessage(0, Waypoint: new Vec3d(30_000, 260, 0)),
            new ApproachResultMessage(1001, true),
            new DockRequestMessage(3, false),
            new DockResultMessage(false, true),
            new MarketRequestMessage(11),
            new MarketStateMessage(11, new MarketState(5000, 2, 40, new[]
            {
                new MarketEntry(1, 158, 2),
                new MarketEntry(2, 120, 0),
            })),
            new TradeRequestMessage(12, 1, 2, true),
            new TradeResultMessage(12, true, "Purchased water ×2 for 24 credits.", "Куплено: вода ×2 за 24 кредитов.",
                new MarketState(4976, 2, 40, new[] { new MarketEntry(1, 158, 2) })),
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void AllMessagesRoundTrip(IProtocolMessage original)
    {
        var bytes = ProtocolCodec.Encode(original);
        Assert.True(bytes.Length <= ProtocolConstants.MaxPacketSize);
        Assert.True(ProtocolCodec.TryDecode(bytes, out var decoded, out var error), error);
        Assert.NotNull(decoded);
        Assert.Equal(original.Type, decoded!.Type);

        if (original is SnapshotMessage snapshot && decoded is SnapshotMessage decodedSnapshot)
        {
            Assert.Equal(snapshot.Tick, decodedSnapshot.Tick);
            Assert.Equal(snapshot.ServerTimeMs, decodedSnapshot.ServerTimeMs);
            Assert.Equal(snapshot.Entities, decodedSnapshot.Entities);
        }
        else if (original is MarketStateMessage market && decoded is MarketStateMessage decodedMarket)
        {
            Assert.Equal(market.RequestId, decodedMarket.RequestId);
            AssertMarketState(market.State, decodedMarket.State);
        }
        else if (original is TradeResultMessage trade && decoded is TradeResultMessage decodedTrade)
        {
            Assert.Equal(trade.RequestId, decodedTrade.RequestId);
            Assert.Equal(trade.Success, decodedTrade.Success);
            Assert.Equal(trade.Message, decodedTrade.Message);
            Assert.Equal(trade.RussianMessage, decodedTrade.RussianMessage);
            AssertMarketState(trade.State, decodedTrade.State);
        }
        else
        {
            Assert.Equal(original, decoded);
        }
    }

    private static void AssertMarketState(MarketState expected, MarketState actual)
    {
        Assert.Equal(expected.Credits, actual.Credits);
        Assert.Equal(expected.CargoUsed, actual.CargoUsed);
        Assert.Equal(expected.CargoCapacity, actual.CargoCapacity);
        Assert.Equal(expected.Entries, actual.Entries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidImpactStateIsRejected(int invalid)
    {
        var impact=new ImpactState(1,invalid==0?new Vec3d(double.NaN,0,0):Vec3d.Zero,
            invalid==1?Vec3d.Zero:Vec3d.Up,invalid==2?float.NaN:20,10);
        var packet=ProtocolCodec.Encode(new SnapshotMessage(1,1,new[]
        {
            new EntitySnapshot(1,EntityKind.Player,"Impact",Vec3d.Zero,0,0,Vec3d.Zero,0,System.Numerics.Quaternion.Identity,Impact:impact),
        }));
        Assert.False(ProtocolCodec.TryDecode(packet,out _,out _));
    }

    [Fact]
    public void WrongProtocolVersionIsRejected()
    {
        var bytes = ProtocolCodec.Encode(new PingMessage(1));
        bytes[2] = (byte)(ProtocolConstants.Version + 1);

        Assert.False(ProtocolCodec.TryDecode(bytes, out _, out var error));
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(ProtocolCodec.TryReadHeader(bytes, out var version, out var type));
        Assert.Equal(ProtocolConstants.Version + 1, version);
        Assert.Equal(MessageType.Ping, type);
    }

    [Fact]
    public void BadMagicAndTruncatedPacketsAreRejected()
    {
        var bytes = ProtocolCodec.Encode(new SnapshotMessage(3, 4, new[]
        {
            new EntitySnapshot(1, EntityKind.Player, "A", Vec3d.Zero, 0, 0, Vec3d.Zero, 0),
        }));

        var badMagic = bytes.ToArray();
        badMagic[0] = (byte)'X';
        Assert.False(ProtocolCodec.TryDecode(badMagic, out _, out var magicError));
        Assert.Contains("magic", magicError, StringComparison.OrdinalIgnoreCase);

        var truncated = bytes.AsSpan(0, bytes.Length / 2).ToArray();
        Assert.False(ProtocolCodec.TryDecode(truncated, out _, out _));

        Assert.False(ProtocolCodec.TryDecode(Array.Empty<byte>(), out _, out _));
    }

    [Fact]
    public void OversizedStringsAndSnapshotsAreRejectedOnEncode()
    {
        Assert.Throws<ArgumentException>(() =>
            ProtocolCodec.Encode(new HelloMessage(Guid.NewGuid(), new string('x', 200), SurfaceMesh.Fingerprint)));

        var entities = Enumerable.Range(0, ProtocolConstants.MaxEntitiesPerSnapshot + 1)
            .Select(i => new EntitySnapshot((uint)i, EntityKind.Npc, "n", Vec3d.Zero, 0, 0, Vec3d.Zero, 0))
            .ToArray();
        Assert.Throws<ArgumentException>(() => ProtocolCodec.Encode(new SnapshotMessage(1, 1, entities)));
    }

    [Fact]
    public void LargeSnapshotSplitsWithoutDroppingEntities()
    {
        var entities = Enumerable.Range(0, 70)
            .Select(i => new EntitySnapshot((uint)i, EntityKind.Npc, new string('n', 48), Vec3d.Zero, 0, 0, Vec3d.Zero, 0))
            .ToArray();
        var packets = ProtocolCodec.EncodeSnapshotPackets(new SnapshotMessage(12, 34, entities));
        Assert.True(packets.Count > 1);
        var decoded = packets.Select(packet =>
        {
            Assert.True(packet.Length <= ProtocolConstants.MaxPacketSize);
            Assert.True(ProtocolCodec.TryDecode(packet, out var message, out var error), error);
            var snapshot = Assert.IsType<SnapshotMessage>(message);
            Assert.Equal(12ul, snapshot.Tick);
            return snapshot.Entities;
        }).SelectMany(items => items).ToArray();
        Assert.Equal(entities, decoded);
    }

    [Fact]
    public void UnknownMessageTypeIsRejected()
    {
        var bytes = ProtocolCodec.Encode(new PingMessage(1));
        bytes[3] = 250;
        Assert.False(ProtocolCodec.TryDecode(bytes, out _, out var error));
        Assert.Contains("Unknown message type", error);
    }

    [Fact]
    public void HeaderLimitsPacketSize()
    {
        var oversized = new byte[ProtocolConstants.MaxPacketSize + 1];
        Assert.False(ProtocolCodec.TryDecode(oversized, out _, out var error));
        Assert.Contains("exceeds", error);
    }
}
