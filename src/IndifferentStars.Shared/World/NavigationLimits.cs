using System;
using System.Globalization;

namespace IndifferentStars.Shared.World;

public static class NavigationLimits
{
    public const double AstronomicalUnit = 149_597_870_700;
    public const double MaximumDistance = 100 * AstronomicalUnit;
    public const double LocalWarpMinimum = 200;

    public static double CelestialWarpMinimum(CelestialBody body) =>
        body.Kind == CelestialKind.Star ? 1_000_000 : body.AtmosphereHeight + 5000;

    public static double OrbitMinimum(CelestialBody body) =>
        Math.Max(CelestialWarpMinimum(body), body.AtmosphereHeight + WorldCatalog.CruiseClearance + 1000);

    public static bool ValidDistance(double value) => double.IsFinite(value) && value > 0 && value <= MaximumDistance;

    public static bool TryParseDistance(string text, out double metres)
    {
        metres = 0;
        var value = text.Trim().ToLowerInvariant().Replace(" ", "").Replace(',', '.');
        var factor = 1000d;
        foreach (var (unit, multiplier) in new[]
        {
            ("а.е.", AstronomicalUnit), ("а.е", AstronomicalUnit), ("ае", AstronomicalUnit),
            ("a.u.", AstronomicalUnit), ("au", AstronomicalUnit),
            ("км", 1000d), ("km", 1000d), ("м", 1d), ("m", 1d),
        })
        {
            if (!value.EndsWith(unit, StringComparison.Ordinal)) continue;
            factor = multiplier;
            value = value[..^unit.Length];
            break;
        }
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        metres = number * factor;
        return ValidDistance(metres);
    }
}
