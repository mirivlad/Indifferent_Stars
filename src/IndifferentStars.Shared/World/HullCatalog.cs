using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Shared.World;

public sealed record HullDefinition
{
    public required ushort Id { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Class { get; init; }
    public required string Model { get; init; }
    public required double[] Dimensions { get; init; }
    public required double Scale { get; init; }
    public required double MassKg { get; init; }
    public required double Thrust { get; init; }
    public required double ManeuverThrust { get; init; }
    public required double BrakeThrust { get; init; }
    public required double TurnFactor { get; init; }
    public required double MaxSpeed { get; init; }
    public required double BoostSpeed { get; init; }
    public required int CargoCapacity { get; init; }
    public required int NpcLoad { get; init; }
    public required double ReactorOutput { get; init; }
    public required double HullIntegrity { get; init; }
    public required double ScanRange { get; init; }
    public required double TacticalRange { get; init; }
    public required int MainNozzles { get; init; }
    public required int ManeuverNozzles { get; init; }
    public required string[] EquipmentSlots { get; init; }
    public required string[] Hardpoints { get; init; }
    public required string PilotEye { get; init; }
    public string? DockMount { get; init; }
    public WeaponKind[] DefaultWeapons { get; init; } = [];
    public NpcOccupation? WorkerRole { get; init; }
    public StationPurpose? StationPurpose { get; init; }
    public double Radius => Surface.Radius * Scale;
    public SurfaceMesh Surface { get; internal set; } = null!;
    public string CollisionKey => FormattableString.Invariant($"{Surface.SourceHash}@{Scale:R}");
}

public static class HullCatalog
{
    public const ushort Courier = 1;
    public const ushort LegacyWorker = 0;
    public static IReadOnlyList<HullDefinition> All { get; }
    public static string Fingerprint { get; }

    private static readonly Dictionary<ushort, HullDefinition> ById;

    static HullCatalog()
    {
        using var stream = typeof(HullCatalog).Assembly.GetManifestResourceStream("IndifferentStars.Shared.World.Data.hulls.json")
            ?? throw new InvalidDataException("Missing ship hull catalog.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var data = bytes.ToArray();
        var hulls = JsonSerializer.Deserialize<HullDefinition[]>(data, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<StationPurpose>(), new JsonStringEnumConverter<NpcOccupation>(),
                new JsonStringEnumConverter<WeaponKind>() },
        }) ?? throw new InvalidDataException("Missing ship hull definitions.");

        foreach (var hull in hulls)
        {
            if (hull.Id == LegacyWorker || hull.Key.Length == 0 || !hull.Key.All(char.IsAsciiLetterLower) || hull.Dimensions.Length != 3 ||
                hull.Dimensions.Any(value => !double.IsFinite(value) || value <= 0) ||
                hull.Scale <= 0 || hull.MassKg <= 0 || hull.Thrust <= 0 || hull.ManeuverThrust <= 0 ||
                hull.TurnFactor <= 0 || hull.BrakeThrust <= 0 || hull.MaxSpeed <= 0 || hull.BoostSpeed < hull.MaxSpeed ||
                hull.CargoCapacity <= 0 || hull.NpcLoad <= 0 || hull.NpcLoad > hull.CargoCapacity ||
                hull.ReactorOutput <= 0 || hull.HullIntegrity <= 0 || hull.ScanRange <= 0 || hull.TacticalRange < hull.ScanRange ||
                hull.MainNozzles < 1 || hull.ManeuverNozzles < 0 || hull.DefaultWeapons.Length > CombatStatusMessage.MaxSlots ||
                hull.DefaultWeapons.Any(kind => kind == WeaponKind.None || !Enum.IsDefined(kind)))
                throw new InvalidDataException($"Invalid ship hull '{hull.Key}'.");
            hull.Surface = hull.Model == "ship_player" ? SurfaceMesh.Ship : SurfaceMesh.Load(hull.Model);
        }
        All = Array.AsReadOnly(hulls);
        ById = hulls.ToDictionary(h => h.Id);
        var wren = ById[Courier];
        ById.Add(LegacyWorker, wren with
        {
            Id = LegacyWorker,
            Scale = ModelPose.NpcScale,
            MassKg = wren.MassKg * Math.Pow(ModelPose.NpcScale, 3),
        });
        var catalogHash = Convert.ToHexString(SHA256.HashData(data));
        var ships = string.Join('|', hulls.Select(hull => hull.Surface.SourceHash));
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.ASCII.GetBytes(SurfaceMesh.LegacyFingerprint + catalogHash + ships)));
    }

    public static HullDefinition Get(ushort id) => ById[id];
    public static bool Contains(ushort id) => ById.ContainsKey(id);
    public static HullDefinition? Find(string key) => All.FirstOrDefault(hull =>
        hull.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    public static ushort ForWorker(NpcOccupation role, StationPurpose purpose, int roleSlot)
    {
        var eligible = All.Where(hull => hull.WorkerRole == role &&
            (hull.StationPurpose is null || hull.StationPurpose == purpose)).ToArray();
        return eligible.Length == 0 ? LegacyWorker : eligible[roleSlot % eligible.Length].Id;
    }
}
