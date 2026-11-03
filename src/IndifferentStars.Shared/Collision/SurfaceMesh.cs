using System.Numerics;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Shared.Collision;

public readonly record struct Triangle(Vec3d A, Vec3d B, Vec3d C)
{
    public Bounds Bounds => new(
        new(Math.Min(A.X, Math.Min(B.X, C.X)), Math.Min(A.Y, Math.Min(B.Y, C.Y)), Math.Min(A.Z, Math.Min(B.Z, C.Z))),
        new(Math.Max(A.X, Math.Max(B.X, C.X)), Math.Max(A.Y, Math.Max(B.Y, C.Y)), Math.Max(A.Z, Math.Max(B.Z, C.Z))));
}

public readonly record struct Bounds(Vec3d Min, Vec3d Max)
{
    public bool Intersects(Bounds other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    public Bounds Expand(double padding) => new(Min - new Vec3d(padding, padding, padding), Max + new Vec3d(padding, padding, padding));
    public Bounds Move(Vec3d delta) => new(Min + delta, Max + delta);
    public Bounds Union(Bounds other) => new(
        new(Math.Min(Min.X, other.Min.X), Math.Min(Min.Y, other.Min.Y), Math.Min(Min.Z, other.Min.Z)),
        new(Math.Max(Max.X, other.Max.X), Math.Max(Max.Y, other.Max.Y), Math.Max(Max.Z, other.Max.Z)));
}

public sealed class SurfaceMesh
{
    public sealed record Node(int Index, Bounds Bounds, int Start, int Count, Node? Left, Node? Right);
    private int _nodeCount;
    public int NodeCount => _nodeCount;
    public Triangle[] Triangles { get; }
    public Bounds[] TriangleBounds { get; }
    public Node Root { get; }
    public double Radius { get; }
    public string SourceHash { get; }

    private SurfaceMesh(Triangle[] triangles, string hash)
    {
        Triangles = triangles;
        SourceHash = hash;
        Radius = triangles.Max(t => Math.Max(t.A.Length, Math.Max(t.B.Length, t.C.Length)));
        Root = Build(0, triangles.Length);
        TriangleBounds = Triangles.Select(triangle => triangle.Bounds).ToArray();
    }

    private Node Build(int start, int count)
    {
        var index = _nodeCount++;
        var bounds = Triangles[start].Bounds;
        for (var i = start + 1; i < start + count; i++) bounds = bounds.Union(Triangles[i].Bounds);
        if (count <= 8) return new(index, bounds, start, count, null, null);
        var size = bounds.Max - bounds.Min;
        var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        static double Center(Triangle t, int axis) => axis switch
        {
            0 => t.A.X + t.B.X + t.C.X,
            1 => t.A.Y + t.B.Y + t.C.Y,
            _ => t.A.Z + t.B.Z + t.C.Z,
        };
        Array.Sort(Triangles, start, count, Comparer<Triangle>.Create((a, b) => Center(a, axis).CompareTo(Center(b, axis))));
        var half = count / 2;
        return new(index, bounds, start, count, Build(start, half), Build(start + half, count - half));
    }

    public static SurfaceMesh Compose(IEnumerable<Triangle> triangles,string hash) => new(triangles.ToArray(),hash);

    public static SurfaceMesh Load(string name)
    {
        using var stream = typeof(SurfaceMesh).Assembly.GetManifestResourceStream(
            $"IndifferentStars.Shared.Collision.{name}.bin") ?? throw new InvalidDataException($"Missing collision surface: {name}");
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "ISCM") throw new InvalidDataException("Unknown surface format.");
        var hash = Convert.ToHexStringLower(reader.ReadBytes(32));
        var count = reader.ReadInt32();
        if (count <= 0 || count > 1_000_000) throw new InvalidDataException("Invalid triangle count.");
        Vec3d Vertex() => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var triangles = new Triangle[count];
        for (var i = 0; i < count; i++) triangles[i] = new(Vertex(), Vertex(), Vertex());
        return new(triangles, hash);
    }

    public static Vec3d Rotate(Vec3d v, Quaternion q)
    {
        var axis = new Vec3d(q.X, q.Y, q.Z);
        var twice = Vec3d.Cross(axis, v) * 2;
        return v + twice * q.W + Vec3d.Cross(axis, twice);
    }

    public static string Fingerprint => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Ship.SourceHash + Station.SourceHash + Asteroids[0].SourceHash +
            FormattableString.Invariant($"|pose-v2|assembly-{StationAssembly.Version}|{ModelPose.StationScale:R}|{ModelPose.StationSpin:R}|{ModelPose.NpcScale:R}|{ModelPose.StationDockingOffset.X:R}|{ModelPose.StationDockingOffset.Y:R}|{ModelPose.StationDockingOffset.Z:R}"))));

    public static readonly SurfaceMesh Ship = Load("ship_player");
    public static readonly SurfaceMesh Station = Load("station_kestrel");
    public static readonly SurfaceMesh[] Asteroids = { Load("asteroid_0"), Load("asteroid_1"), Load("asteroid_2") };
}

/// <summary>The same model scale and orientation contract is used by the renderer and server.</summary>
public static class ModelPose
{
    public const double StationScale = 275;
    public const double StationSpin = 0.012;
    public const double NpcScale = 0.82;
    public static readonly Vec3d StationDockingOffset = new(0, StationScale * 4.48 + 13.6, 0);
    public static Quaternion StationRotation(StaticBody body, double seconds) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(body.Rotation + seconds * StationSpin));

    public static Quaternion AsteroidRotation(StaticBody body, int seed)
    {
        var random = new SeedStream(seed,body.Id,"asteroid-rotation");
        return Quaternion.CreateFromYawPitchRoll(body.Rotation,
            (float)(random.NextDouble() * Math.PI * 2),
            (float)(random.NextDouble() * Math.PI * 2 - Math.PI));
    }

    public static double AsteroidScale(StaticBody body) => body.Radius * 1.08;
}
