namespace IndifferentStars.Shared.World;

/// <summary>Versioned deterministic substreams; independent of runtime Random and string hashing.</summary>
public sealed class SeedStream
{
    private ulong _state;
    public SeedStream(int seed, int system, string domain)
    {
        _state = (uint)seed | ((ulong)(uint)system << 32);
        foreach (var c in domain) _state = (_state ^ c) * 1099511628211UL;
        Next();
    }
    public ulong Next()
    {
        var z = _state += 0x9e3779b97f4a7c15UL;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
        z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
        return z ^ (z >> 31);
    }
    public double NextDouble() => Unit();
    public int Next(int minimum, int maximum) => Range(minimum, maximum);
    public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
    public int Range(int minimum, int maximum) => minimum + (int)(Next() % (uint)(maximum - minimum));
    public double Between(double minimum, double maximum) => minimum + Unit() * (maximum - minimum);
    public string Name()
    {
        var a = new[] { "Ve", "Ca", "Or", "Ne", "As", "Io", "Thae", "Ly", "Sa", "Ke", "El", "Va" };
        var b = new[] { "sper", "lora", "dris", "thea", "rion", "mora", "lune", "sira", "strel", "neth", "dara", "varis" };
        return a[Range(0,a.Length)] + b[Range(0,b.Length)];
    }
}
