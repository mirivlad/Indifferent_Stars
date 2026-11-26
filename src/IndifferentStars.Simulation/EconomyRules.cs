using System;
using IndifferentStars.Shared.World;
using static IndifferentStars.Shared.World.CommodityCatalog;

namespace IndifferentStars.Simulation;

public readonly record struct StockAmount(ushort Commodity, int Units);
public sealed record ProductionRecipe(StockAmount Output, params StockAmount[] Inputs);

public static class EconomyRules
{
    private static readonly ProductionRecipe[] Exchange =
    {
        new(new(Water, 11)),
        new(new(Food, 8), new StockAmount(Water, 3)),
        new(new(Fuel, 8), new StockAmount(Water, 2)),
    };
    private static readonly ProductionRecipe[] Refinery =
    {
        new(new(Metal, 5), new StockAmount(Ore, 7), new StockAmount(Fuel, 2)),
    };
    private static readonly ProductionRecipe[] Assembly =
    {
        new(new(Components, 4), new StockAmount(Metal, 5), new StockAmount(Fuel, 1)),
    };
    private static readonly ProductionRecipe[] Laboratory =
    {
        new(new(Medicine, 3), new StockAmount(Water, 2), new StockAmount(Food, 1)),
        new(new(Equipment, 2), new StockAmount(Components, 3), new StockAmount(Metal, 1), new StockAmount(Medicine, 1)),
    };

    public static ReadOnlySpan<ProductionRecipe> Recipes(StationPurpose purpose) => purpose switch
    {
        StationPurpose.Trade => Exchange,
        StationPurpose.Mining => Refinery,
        StationPurpose.Freight => Assembly,
        _ => Laboratory,
    };

    public static int InitialStock(StationPurpose purpose, Commodity good) => (purpose, good.Id) switch
    {
        (StationPurpose.Mining, Ore) => good.InitialStock + 65,
        (StationPurpose.Freight, Components) => good.InitialStock + 22,
        (StationPurpose.Research, Equipment) => good.InitialStock + 12,
        _ => good.InitialStock,
    };

    public static int Capacity(Commodity good) => good.InitialStock * 4;

    public static int ExportReserve(Commodity good) => Math.Max(2, good.InitialStock / 12);
    public const double ProductionInterval = 15;
    public const int InitialTreasury = 30_000;
    public const int StartingFreightCapital = 8000;
    public const int TreasuryLimit = 180_000;
}
