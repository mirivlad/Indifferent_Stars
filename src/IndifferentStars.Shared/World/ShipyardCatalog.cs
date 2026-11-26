using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Shared.World;

public enum ModuleCategory : byte { Engine, Reactor, Shield, Sensor, Cargo, Armor, Weapon, Mining }

public sealed record ModuleDefinition(ushort Id, string Key, ModuleCategory Category, int Price, double MassKg,
    double PowerDraw, double Bonus, WeaponKind Weapon = WeaponKind.None);

public readonly record struct FittedModule(byte Slot, ushort ModuleId);

public sealed record ShipFit(double MassKg, double ThrustFactor, double Reactor, double Draw, double Shield,
    double Armor, double ScanRange, double TacticalRange, int CargoCapacity, double MiningFactor, WeaponKind[] Weapons)
{
    public double AvailableEnergy => Reactor - Draw;
}

public static class ShipyardCatalog
{
    public static IReadOnlyList<ModuleDefinition> Modules { get; } = Array.AsReadOnly(new ModuleDefinition[]
    {
        new(1, "vector_drive", ModuleCategory.Engine, 1150, 1250, 8, 1.25),
        new(2, "aux_reactor", ModuleCategory.Reactor, 1280, 1450, 0, 27),
        new(3, "layer_shield", ModuleCategory.Shield, 980, 1100, 9, 29),
        new(4, "deep_sensor", ModuleCategory.Sensor, 820, 480, 7, 1.5),
        new(5, "cargo_rack", ModuleCategory.Cargo, 640, 870, 2, 26),
        new(6, "woven_armor", ModuleCategory.Armor, 940, 1700, 1, 34),
        new(7, "ore_cutter", ModuleCategory.Mining, 1380, 2100, 14, 1.7),
        new(8, "pulse_mount", ModuleCategory.Weapon, 720, 650, 6, 0, WeaponKind.PulseLaser),
        new(9, "beam_mount", ModuleCategory.Weapon, 1320, 980, 15, 0, WeaponKind.BeamLaser),
        new(10, "cannon_mount", ModuleCategory.Weapon, 1120, 1650, 7, 0, WeaponKind.Cannon),
        new(11, "missile_mount", ModuleCategory.Weapon, 1410, 1110, 9, 0, WeaponKind.Missile),
        new(12, "turret_mount", ModuleCategory.Weapon, 870, 830, 5, 0, WeaponKind.Turret),
    });

    public static ModuleDefinition? Module(ushort id) => id >= 1 && id <= Modules.Count ? Modules[id - 1] : null;

    public static int HullPrice(ushort id) => id switch
    {
        1 => 1850, 2 => 4750, 3 => 5650, 4 => 3900, 5 => 14200, 6 => 6200,
        7 => 11900, 8 => 8300, 9 => 35700, 10 => 1450, 11 => 5500, 12 => 16800,
        13 => 7100, 14 => 5000, 15 => 7050, 16 => 13500, 17 => 9450,
        _ => 0,
    };

    public static bool SellsHull(StationPurpose purpose, ushort id) => purpose switch
    {
        StationPurpose.Trade => id is 1 or 2 or 6 or 10 or 11 or 15,
        StationPurpose.Mining => id is 1 or 3 or 10 or 13,
        StationPurpose.Research => id is 1 or 4 or 10 or 14,
        StationPurpose.Freight => id is 2 or 5 or 7 or 8 or 9 or 11 or 12 or 16 or 17,
        _ => false,
    };

    public static bool SellsModule(StationPurpose purpose, ModuleDefinition module) => module.Category switch
    {
        ModuleCategory.Mining => purpose == StationPurpose.Mining,
        ModuleCategory.Weapon when module.Weapon is WeaponKind.BeamLaser or WeaponKind.Missile => purpose == StationPurpose.Freight,
        ModuleCategory.Armor => purpose is StationPurpose.Trade or StationPurpose.Freight,
        _ => true,
    };

    public static int SlotCount(HullDefinition hull) => hull.EquipmentSlots.Length + hull.DefaultWeapons.Length;

    public static ModuleCategory? SlotCategory(HullDefinition hull, int index)
    {
        if (index < 0 || index >= SlotCount(hull)) return null;
        if (index >= hull.EquipmentSlots.Length) return ModuleCategory.Weapon;
        return hull.EquipmentSlots[index] switch
        {
            "Engine" => ModuleCategory.Engine,
            "Reactor" => ModuleCategory.Reactor,
            "Sensor" => ModuleCategory.Sensor,
            "Shield" => ModuleCategory.Shield,
            "Cargo" => ModuleCategory.Cargo,
            "Mining" => ModuleCategory.Mining,
            "Defence" or "Utility" => ModuleCategory.Armor,
            _ => null,
        };
    }

    public static ShipFit Calculate(HullDefinition hull, FittedModule[] modules)
    {
        var mass = hull.MassKg;
        var thrust = 1d;
        var reactor = hull.ReactorOutput;
        var draw = 0d;
        var shield = hull.HullIntegrity * .55;
        var armor = hull.HullIntegrity * .40;
        var scan = hull.ScanRange;
        var tactical = hull.TacticalRange;
        var cargo = hull.CargoCapacity;
        var mining = 1d;
        var weapons = modules.Length == 0 ? hull.DefaultWeapons : hull.DefaultWeapons.ToArray();
        foreach (var fitted in modules)
        {
            var module = Module(fitted.ModuleId)!;
            mass += module.MassKg;
            draw += module.PowerDraw;
            switch (module.Category)
            {
                case ModuleCategory.Engine: thrust *= module.Bonus; break;
                case ModuleCategory.Reactor: reactor += module.Bonus; break;
                case ModuleCategory.Shield: shield += module.Bonus; break;
                case ModuleCategory.Armor: armor += module.Bonus; break;
                case ModuleCategory.Cargo: cargo += (int)module.Bonus; break;
                case ModuleCategory.Sensor: scan *= module.Bonus; tactical *= module.Bonus; break;
                case ModuleCategory.Mining: mining *= module.Bonus; break;
                case ModuleCategory.Weapon: weapons[fitted.Slot - hull.EquipmentSlots.Length] = module.Weapon; break;
            }
        }
        return new(mass, thrust * Math.Sqrt(hull.MassKg / mass), reactor, draw, shield, armor, scan, tactical, cargo, mining, weapons);
    }

    public static bool ValidFit(HullDefinition hull, FittedModule[] modules)
    {
        if (modules.Length > SlotCount(hull) || modules.Select(module => module.Slot).Distinct().Count() != modules.Length ||
            modules.Any(fitted => Module(fitted.ModuleId) is not { } module || SlotCategory(hull, fitted.Slot) != module.Category))
            return false;
        var fit = Calculate(hull, modules);
        return fit.MassKg <= hull.MassKg * 1.28 && fit.Draw <= fit.Reactor * .42;
    }
}
