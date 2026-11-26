using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Server;

internal sealed record SavedProfile(SavedPlayer Player, Guid TradeSession, TradeResultMessage? LastTrade);

internal sealed record WorldSave(
    int FormatVersion, int GeneratorVersion, int Seed, int SystemIndex, string Geometry,
    SavedSimulation Simulation, Dictionary<string, SavedProfile> Profiles,
    Dictionary<ushort, string>? HullGeometry = null);

internal sealed class WorldSaveStore : IDisposable
{
    private const int CurrentVersion = 5;
    private readonly string _path;
    private readonly FileStream _lease;
    private byte[]? _lastGood;
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    if (info.Type == typeof(ShipState))
                        for (var i = info.Properties.Count - 1; i >= 0; i--)
                        {
                            var property = info.Properties[i];
                            if (property.Set is null || property.Name is nameof(ShipState.Yaw) or nameof(ShipState.Pitch))
                                info.Properties.RemoveAt(i);
                        }
                },
            },
        },
        Converters = { new VectorConverter() },
    };

    public WorldSaveStore(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public WorldSave? Load(WorldCatalog catalog)
    {
        var primary = TryRead(_path, catalog, out var primaryFound);
        if (primary is not null) return primary;
        var backup = TryRead(_path + ".bak", catalog, out var backupFound);
        if (backup is not null)
        {
            Console.Error.WriteLine("Recovering the world from its last valid backup.");
            return backup;
        }
        if (primaryFound || backupFound)
            throw new InvalidDataException("Both world save copies are invalid. Preserve them before starting a new world.");
        return null;
    }

    private WorldSave? TryRead(string path, WorldCatalog catalog, out bool found)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { found = false; return null; }
        found = true;
        try
        {
            if (bytes.Length <= 65 || bytes[64] != (byte)'\n' ||
                Encoding.ASCII.GetString(bytes, 0, 64) != Convert.ToHexString(SHA256.HashData(bytes.AsSpan(65))))
                throw new InvalidDataException("Save checksum mismatch.");
            var save = JsonSerializer.Deserialize<WorldSave>(bytes.AsSpan(65), Json)
                ?? throw new InvalidDataException("Missing saved world.");
            // Format 1 ships have no hull ID. The default keeps the original
            // player and worker hulls, including their exact collision scales.
            if (save.FormatVersion is < 1 or > CurrentVersion ||
                (save.FormatVersion >= 4 && save.Simulation.Security is null) ||
                (save.FormatVersion >= 5 && save.Simulation.Economy is null) ||
                save.GeneratorVersion != catalog.GeneratorVersion ||
                save.Seed != catalog.Seed || save.SystemIndex != catalog.SystemIndex || save.Geometry != SurfaceMesh.LegacyFingerprint)
                throw new InvalidOperationException("Save format, generator, world seed, system or collision geometry differs.");
            if (save.FormatVersion >= 2)
            {
                var ids = save.Simulation.Npcs.Select(ship => ship.EffectiveHullId)
                    .Concat(save.Profiles.Values.Select(profile => profile.Player.Ship.EffectiveHullId)).Distinct();
                if (save.HullGeometry is null || ids.Any(id => !HullCatalog.Contains(id) ||
                        save.HullGeometry.GetValueOrDefault(id) != HullCatalog.Get(id).CollisionKey))
                    throw new InvalidOperationException("The saved ship hull geometry differs.");
            }
            _lastGood = bytes;
            return save;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Invalid world save '{path}': {error.Message}");
            return null;
        }
    }

    public void Write(GameWorld world, Dictionary<string, SavedProfile> profiles)
    {
        var geometry = world.Npcs.Select(ship => ship.EffectiveHullId)
            .Concat(profiles.Values.Select(profile => profile.Player.Ship.EffectiveHullId)).Distinct()
            .ToDictionary(id => id, id => HullCatalog.Get(id).CollisionKey);
        var save = new WorldSave(CurrentVersion, world.Catalog.GeneratorVersion, world.Catalog.Seed,
            world.Catalog.SystemIndex, SurfaceMesh.LegacyFingerprint, world.CaptureSimulation(), profiles, geometry);
        var payload = JsonSerializer.SerializeToUtf8Bytes(save, Json);
        var blob = new byte[65 + payload.Length];
        Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(payload)) + "\n", blob);
        payload.CopyTo(blob, 65);
        if (_lastGood is not null) Replace(_path + ".bak", _lastGood);
        Replace(_path, blob);
        _lastGood = blob;
    }

    private static void Replace(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            output.Write(bytes);
            output.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    public void Dispose() => _lease.Dispose();

    private sealed class VectorConverter : JsonConverter<Vec3d>
    {
        public override Vec3d Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a 3D vector.");
            double Next(ref Utf8JsonReader source) =>
                source.Read() && source.TokenType == JsonTokenType.Number ? source.GetDouble() : throw new JsonException("Invalid vector coordinate.");
            var value = new Vec3d(Next(ref reader), Next(ref reader), Next(ref reader));
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray || !value.IsFinite)
                throw new JsonException("Invalid 3D vector.");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, Vec3d value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }
}
