using System.Collections.Generic;

namespace IndifferentStars.Shared.World;

/// <summary>
/// Reference prices and normal stock for one tradable good. Stations quote live prices.
/// </summary>
public sealed record Commodity(ushort Id, string Key, int BuyPrice, int SellPrice, int InitialStock);

/// <summary>
/// The common commodity table shared by the server and the client. Names are not
/// stored here: both sides localize <c>commodity_&lt;key&gt;</c> so the same trade
/// data is readable in every supported language.
/// </summary>
public static class CommodityCatalog
{
    public const ushort Water = 1;
    public const ushort Food = 2;
    public const ushort Medicine = 3;
    public const ushort Fuel = 4;
    public const ushort Ore = 5;
    public const ushort Components = 6;
    public const ushort Metal = 7;
    public const ushort Equipment = 8;
    public const int CargoCapacity = 40;
    public const int StartingCredits = 5000;
    public const int MaxTradeQuantity = 1000;

    public static readonly IReadOnlyList<Commodity> All = new[]
    {
        new Commodity(Water, "water", 12, 8, 160),
        new Commodity(Food, "food", 22, 15, 120),
        new Commodity(Medicine, "medicine", 65, 45, 48),
        new Commodity(Fuel, "fuel", 30, 20, 200),
        new Commodity(Ore, "ore", 48, 34, 90),
        new Commodity(Components, "components", 90, 64, 36),
        new Commodity(Metal, "metal", 72, 50, 48),
        new Commodity(Equipment, "equipment", 158, 117, 24),
    };

    public static Commodity? Find(ushort id)
    {
        foreach (var commodity in All)
        {
            if (commodity.Id == id)
                return commodity;
        }

        return null;
    }
}
