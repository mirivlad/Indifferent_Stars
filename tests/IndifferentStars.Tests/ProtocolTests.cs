using System;
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
            new HelloMessage(Guid.Parse("b5c6d7e8-1111-2222-3333-444455556666"), "Captain Vega"),
            new HelloAcceptedMessage(
                7, "Captain Vega", new Vec3d(10, 20, -30), 1.2f, 20261101,
                "Vesper — Kestrel Reach", 4200, 21000, 720, 42, 20),
            new HelloRejectedMessage("Protocol mismatch: client v0, server v1."),
            new InputMessage(1234, 127, -127, 64, -32, true, false),
            new SnapshotMessage(100, 5000, new[]
            {
                new EntitySnapshot(7, EntityKind.Player, "Captain Vega", new Vec3d(1.5, 2.5, 3.5), 0.5f, -0.2f, new Vec3d(10, 0, -4), 127),
                new EntitySnapshot(5000, EntityKind.Npc, "Dawn Trader", new Vec3d(-400, 80, 1200), 3.1f, 0.1f, new Vec3d(-40, 0, -70), 90),
            }),
            new ScanRequestMessage(9, 1001),
            new ScanResultMessage(9, 1001, true, "Asteroid KV-1001", "Composition: silicate 40%"),
            new WarpRequestMessage(10, 2),
            new WarpResultMessage(10, 2, true, "Short warp complete", new Vec3d(11000, -1000, -7000), 0.2f, -0.1f),
            new PingMessage(987654321),
            new PongMessage(987654321),
            new DisconnectMessage("leaving"),
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
        else
        {
            Assert.Equal(original, decoded);
        }
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
            ProtocolCodec.Encode(new HelloMessage(Guid.NewGuid(), new string('x', 200))));

        var entities = Enumerable.Range(0, ProtocolConstants.MaxEntitiesPerSnapshot + 1)
            .Select(i => new EntitySnapshot((uint)i, EntityKind.Npc, "n", Vec3d.Zero, 0, 0, Vec3d.Zero, 0))
            .ToArray();
        Assert.Throws<ArgumentException>(() => ProtocolCodec.Encode(new SnapshotMessage(1, 1, entities)));
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