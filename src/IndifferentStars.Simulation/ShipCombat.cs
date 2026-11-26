using System;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed class EquippedWeapon
{
    public WeaponKind Kind { get; init; }
    public int Loaded { get; set; }
    public int Reserve { get; set; }
    public double NextShot { get; set; }
    public double ReloadAt { get; set; }

    public static EquippedWeapon Create(WeaponKind kind)
    {
        var definition = WeaponCatalog.Get(kind);
        return new EquippedWeapon { Kind = kind, Loaded = definition.Magazine, Reserve = definition.SpareRounds };
    }
}

public sealed class ShipCombat
{
    public double Energy { get; set; }
    public double Shield { get; set; }
    public double Armor { get; set; }
    public double Hull { get; set; }
    public double Engines { get; set; } = 1;
    public double Sensors { get; set; } = 1;
    public double Weapons { get; set; } = 1;
    public double LastDamage { get; set; } = -10;
    public bool Destroyed { get; set; }
    public double RecoverAt { get; set; }
    public uint TargetId { get; set; }
    public CombatOrder Order { get; set; }
    public byte SelectedSlot { get; set; }
    public EquippedWeapon[] Slots { get; set; } = Array.Empty<EquippedWeapon>();

    public static ShipCombat Create(HullDefinition hull, ShipFit? fit = null)
    {
        fit ??= ShipyardCatalog.Calculate(hull, Array.Empty<FittedModule>());
        return new ShipCombat
        {
            Energy = fit.AvailableEnergy,
            Shield = fit.Shield,
            Armor = fit.Armor,
            Hull = hull.HullIntegrity,
            Slots = fit.Weapons.Select(EquippedWeapon.Create).ToArray(),
        };
    }
}

public readonly record struct FlightProjectile(
    uint Owner, uint Target, WeaponKind Kind, Vec3d Position, Vec3d Direction, double RangeRemaining, double Damage);
