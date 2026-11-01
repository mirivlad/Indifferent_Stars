using System.Collections.Generic;

namespace IndifferentStars.Shared.World;

/// <summary>
/// A tradable good. Prices are station-relative and deliberately simple for M0:
/// the player buys at <see cref="BuyPrice"/> and sells at <see cref="SellPrice"/>.
/// The spread is what the station earns for handling the cargo.
/// </summary>
public sealed record Commodity(ushort Id, string Key, int BuyPrice, int SellPrice, int InitialStock);

/// <summary>
/// The common commodity table shared by the server and the client. Names are not
/// stored here: both sides localize <c>commodity_&lt;key&gt;</c> so the same trade
/// data is readable in every supported language.
/// </summary>
public static class CommodityCatalog
{
    public const int CargoCapacity = 40;
    public const int StartingCredits = 5000;
    public const int MaxTradeQuantity = 1000;

    public static readonly IReadOnlyList<Commodity> All = new[]
    {
        new Commodity(1, "water", 12, 8, 160),
        new Commodity(2, "food", 22, 15, 120),
        new Commodity(3, "medicine", 65, 45, 48),
        new Commodity(4, "fuel", 30, 20, 200),
        new Commodity(5, "ore", 48, 34, 90),
        new Commodity(6, "components", 90, 64, 36),
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