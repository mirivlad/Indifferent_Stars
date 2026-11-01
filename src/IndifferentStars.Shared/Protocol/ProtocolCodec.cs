using System;
using System.IO;
using System.Text;

namespace IndifferentStars.Shared.Protocol;

/// <summary>
/// Compact little-endian binary codec for the versioned UDP protocol.
/// Every packet is length checked before allocation; malformed packets are rejected.
/// </summary>
public static class ProtocolCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encode(IProtocolMessage message)
    {
        using var stream = new MemoryStream(256);
        using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
        writer.Write(ProtocolConstants.Magic0);
        writer.Write(ProtocolConstants.Magic1);
        writer.Write(ProtocolConstants.Version);
        writer.Write((byte)message.Type);

        switch (message)
        {
            case HelloMessage hello:
                WriteGuid(writer, hello.ClientId);
                WriteString(writer, hello.ClientName, ProtocolConstants.MaxNameBytes);
                break;

            case HelloAcceptedMessage accepted:
                writer.Write(accepted.PlayerId);
                WriteString(writer, accepted.PlayerName, ProtocolConstants.MaxNameBytes);
                WriteVec3(writer, accepted.Spawn);
                writer.Write(accepted.SpawnYaw);
                writer.Write(accepted.WorldSeed);
                WriteString(writer, accepted.SystemName, ProtocolConstants.MaxStringBytes);
                writer.Write(accepted.ScanRange);
                writer.Write(accepted.WarpRange);
                writer.Write(accepted.MaxSpeed);
                writer.Write(accepted.ServerTick);
                writer.Write(accepted.TickRate);
                break;

            case HelloRejectedMessage rejected:
                WriteString(writer, rejected.Reason, ProtocolConstants.MaxStringBytes);
                break;

            case InputMessage input:
                writer.Write(input.Sequence);
                writer.Write(input.Throttle);
                writer.Write(input.Steer);
                writer.Write(input.Pitch);
                writer.Write(input.Vertical);
                writer.Write(input.Boost);
                writer.Write(input.Brake);
                break;

            case SnapshotMessage snapshot:
                writer.Write(snapshot.Tick);
                writer.Write(snapshot.ServerTimeMs);
                if (snapshot.Entities.Length > ProtocolConstants.MaxEntitiesPerSnapshot)
                    throw new ArgumentException("Too many snapshot entities.");
                writer.Write((ushort)snapshot.Entities.Length);
                foreach (var entity in snapshot.Entities)
                {
                    writer.Write(entity.Id);
                    writer.Write((byte)entity.Kind);
                    WriteString(writer, entity.Name, ProtocolConstants.MaxNameBytes);
                    WriteVec3(writer, entity.Position);
                    writer.Write(entity.Yaw);
                    writer.Write(entity.Pitch);
                    WriteVec3(writer, entity.Velocity);
                    writer.Write(entity.Throttle);
                }
                break;

            case ScanRequestMessage scanRequest:
                writer.Write(scanRequest.RequestId);
                writer.Write(scanRequest.TargetId);
                break;

            case ScanResultMessage scanResult:
                writer.Write(scanResult.RequestId);
                writer.Write(scanResult.TargetId);
                writer.Write(scanResult.Success);
                WriteString(writer, scanResult.Title, ProtocolConstants.MaxStringBytes);
                WriteString(writer, scanResult.Body, ProtocolConstants.MaxTextBytes);
                break;

            case WarpRequestMessage warpRequest:
                writer.Write(warpRequest.RequestId);
                writer.Write(warpRequest.TargetId);
                break;

            case WarpResultMessage warpResult:
                writer.Write(warpResult.RequestId);
                writer.Write(warpResult.TargetId);
                writer.Write(warpResult.Success);
                WriteString(writer, warpResult.Message, ProtocolConstants.MaxStringBytes);
                WriteVec3(writer, warpResult.NewPosition);
                writer.Write(warpResult.Yaw);
                writer.Write(warpResult.Pitch);
                break;

            case PingMessage ping:
                writer.Write(ping.ClientTimeMs);
                break;

            case PongMessage pong:
                writer.Write(pong.ClientTimeMs);
                break;

            case DisconnectMessage disconnect:
                WriteString(writer, disconnect.Reason, ProtocolConstants.MaxStringBytes);
                break;

            default:
                throw new ArgumentException($"Unsupported message type {message.GetType().Name}.");
        }

        writer.Flush();
        var bytes = stream.ToArray();
        if (bytes.Length > ProtocolConstants.MaxPacketSize)
            throw new ArgumentException($"Packet too large: {bytes.Length} bytes.");
        return bytes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out IProtocolMessage? message, out string error)
    {
        message = null;
        error = string.Empty;

        if (data.Length > ProtocolConstants.MaxPacketSize)
        {
            error = $"Packet exceeds {ProtocolConstants.MaxPacketSize} bytes.";
            return false;
        }

        if (data.Length < ProtocolConstants.HeaderSize)
        {
            error = "Packet shorter than protocol header.";
            return false;
        }

        if (data[0] != ProtocolConstants.Magic0 || data[1] != ProtocolConstants.Magic1)
        {
            error = "Bad protocol magic.";
            return false;
        }

        var version = data[2];
        if (version != ProtocolConstants.Version)
        {
            error = $"Unsupported protocol version {version} (server speaks {ProtocolConstants.Version}).";
            return false;
        }

        var type = (MessageType)data[3];
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
            reader.BaseStream.Position = ProtocolConstants.HeaderSize;

            message = type switch
            {
                MessageType.Hello => new HelloMessage(
                    ReadGuid(reader),
                    ReadString(reader, ProtocolConstants.MaxNameBytes)),

                MessageType.HelloAccepted => new HelloAcceptedMessage(
                    reader.ReadUInt32(),
                    ReadString(reader, ProtocolConstants.MaxNameBytes),
                    ReadVec3(reader),
                    reader.ReadSingle(),
                    reader.ReadInt32(),
                    ReadString(reader, ProtocolConstants.MaxStringBytes),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadUInt64(),
                    reader.ReadByte()),

                MessageType.HelloRejected => new HelloRejectedMessage(
                    ReadString(reader, ProtocolConstants.MaxStringBytes)),

                MessageType.Input => new InputMessage(
                    reader.ReadUInt32(),
                    reader.ReadSByte(),
                    reader.ReadSByte(),
                    reader.ReadSByte(),
                    reader.ReadSByte(),
                    reader.ReadBoolean(),
                    reader.ReadBoolean()),

                MessageType.Snapshot => ReadSnapshot(reader),

                MessageType.ScanRequest => new ScanRequestMessage(
                    reader.ReadUInt32(),
                    reader.ReadUInt32()),

                MessageType.ScanResult => new ScanResultMessage(
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadBoolean(),
                    ReadString(reader, ProtocolConstants.MaxStringBytes),
                    ReadString(reader, ProtocolConstants.MaxTextBytes)),

                MessageType.WarpRequest => new WarpRequestMessage(
                    reader.ReadUInt32(),
                    reader.ReadUInt32()),

                MessageType.WarpResult => new WarpResultMessage(
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadBoolean(),
                    ReadString(reader, ProtocolConstants.MaxStringBytes),
                    ReadVec3(reader),
                    reader.ReadSingle(),
                    reader.ReadSingle()),

                MessageType.Ping => new PingMessage(reader.ReadUInt64()),
                MessageType.Pong => new PongMessage(reader.ReadUInt64()),
                MessageType.Disconnect => new DisconnectMessage(
                    ReadString(reader, ProtocolConstants.MaxStringBytes)),

                _ => throw new InvalidDataException($"Unknown message type {(byte)type}."),
            };

            if (stream.Position > stream.Length)
                throw new InvalidDataException("Message shorter than declared fields.");

            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or DecoderFallbackException
                                       or InvalidDataException or ArgumentException)
        {
            message = null;
            error = $"Malformed {type} message: {ex.Message}";
            return false;
        }
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> data, out byte version, out MessageType type)
    {
        version = 0;
        type = 0;
        if (data.Length < ProtocolConstants.HeaderSize ||
            data[0] != ProtocolConstants.Magic0 || data[1] != ProtocolConstants.Magic1)
            return false;
        version = data[2];
        type = (MessageType)data[3];
        return true;
    }

    private static SnapshotMessage ReadSnapshot(BinaryReader reader)
    {
        var tick = reader.ReadUInt64();
        var serverTimeMs = reader.ReadUInt64();
        var count = reader.ReadUInt16();
        if (count > ProtocolConstants.MaxEntitiesPerSnapshot)
            throw new InvalidDataException($"Snapshot entity count {count} exceeds limit.");

        var entities = new EntitySnapshot[count];
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadUInt32();
            var kind = (EntityKind)reader.ReadByte();
            if (kind is not (EntityKind.Player or EntityKind.Npc))
                throw new InvalidDataException($"Unknown entity kind {(byte)kind}.");
            entities[i] = new EntitySnapshot(
                id,
                kind,
                ReadString(reader, ProtocolConstants.MaxNameBytes),
                ReadVec3(reader),
                reader.ReadSingle(),
                reader.ReadSingle(),
                ReadVec3(reader),
                reader.ReadSByte());
        }

        return new SnapshotMessage(tick, serverTimeMs, entities);
    }

    private static void WriteGuid(BinaryWriter writer, Guid value) => writer.Write(value.ToByteArray());

    private static Guid ReadGuid(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(16);
        if (bytes.Length != 16)
            throw new EndOfStreamException("Truncated GUID.");
        return new Guid(bytes);
    }

    private static void WriteVec3(BinaryWriter writer, Vec3d value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vec3d ReadVec3(BinaryReader reader) =>
        new(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());

    private static void WriteString(BinaryWriter writer, string value, int maxBytes)
    {
        var bytes = Utf8.GetBytes(value ?? string.Empty);
        if (bytes.Length > maxBytes)
            throw new ArgumentException($"String exceeds {maxBytes} bytes.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader, int maxBytes)
    {
        var length = reader.ReadUInt16();
        if (length > maxBytes)
            throw new InvalidDataException($"String length {length} exceeds {maxBytes} bytes.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("Truncated string.");
        return Utf8.GetString(bytes);
    }
}