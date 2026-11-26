using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Shared.World;

public readonly record struct WeaponDefinition(
    double RangeMetres, double Damage, double Energy, double IntervalSeconds,
    double ProjectileSpeed, double AimConeRadians, double SpreadRadians,
    int Magazine, int SpareRounds, double ReloadSeconds, double TrackingRadiansPerSecond);

public static class WeaponCatalog
{
    public static WeaponDefinition Get(WeaponKind kind) => kind switch
    {
        WeaponKind.PulseLaser => new(2600, 17, 7, .53, 0, .12, .002, 0, 0, 0, 0),
        WeaponKind.BeamLaser => new(2000, 5, 2.6, .10, 0, .17, 0, 0, 0, 0, 0),
        WeaponKind.Cannon => new(4400, 33, 3, .76, 4100, .10, .0038, 10, 40, 2.45, 0),
        WeaponKind.Missile => new(6400, 56, 5, 1.6, 1350, .36, 0, 2, 8, 3.8, 2.2),
        WeaponKind.Turret => new(2950, 8, 2, .25, 2350, 1.58, .004, 28, 112, 1.9, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
