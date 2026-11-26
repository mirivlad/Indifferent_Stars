using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed class StationLedger
{
    public int Credits { get; set; } = EconomyRules.InitialTreasury;
    public Dictionary<ushort, int> Produced { get; set; } = new();
    public Dictionary<ushort, int> Consumed { get; set; } = new();
}

public sealed record SavedEconomy(double NextProduction, Dictionary<int, StationLedger> Stations,
    Dictionary<ushort, int> CargoLost);

public sealed partial class GameWorld
{
    private readonly Dictionary<int, StationLedger> _stationLedgers = new();
    private readonly Dictionary<ushort, int> _cargoLost = new();
    private double _nextProduction = EconomyRules.ProductionInterval;

    private void InitializeEconomy()
    {
        foreach (var station in Catalog.Stations)
        {
            _stationStocks[station.Id] = CommodityCatalog.All.ToDictionary(good => good.Id,
                good => EconomyRules.InitialStock(station.Purpose, good));
            _stationLedgers.Add(station.Id, new());
        }
    }

    private MarketEntry Quote(int stationId, Commodity good, int aboard = 0)
    {
        var stock = _stationStocks[stationId][good.Id];
        var capacity = Math.Max(stock, EconomyRules.Capacity(good));
        var premium = StationPricePremium(stationId, good.Id);
        var shortage = Math.Max(0, good.InitialStock - stock);
        var excess = Math.Max(0, stock - good.InitialStock);
        var modifier = Math.Clamp(1 + (shortage * 1.3 - excess * .55) / good.InitialStock, .43, 2.3);
        var buy = Math.Max(2, Price(good.BuyPrice));
        var sell = Math.Clamp(Price(good.SellPrice), 1, buy - 1);
        var demand = Math.Min(capacity - stock, _stationLedgers[stationId].Credits / sell);
        return new MarketEntry(good.Id, stock, aboard, buy, sell, capacity, demand);

        int Price(int price) => (int)Math.Round(price * modifier + premium);
    }

    private void StepEconomy()
    {
        if (TimeSeconds < _nextProduction) return;
        var cycle = (long)Math.Round(_nextProduction / EconomyRules.ProductionInterval);
        _nextProduction += EconomyRules.ProductionInterval;
        foreach (var station in Catalog.Stations)
        {
            var stock = _stationStocks[station.Id];
            var ledger = _stationLedgers[station.Id];
            foreach (var recipe in EconomyRules.Recipes(station.Purpose))
            {
                var output = recipe.Output;
                var capacity = EconomyRules.Capacity(CommodityCatalog.Find(output.Commodity)!);
                if (stock[output.Commodity] + output.Units > capacity ||
                    recipe.Inputs.Any(input => stock[input.Commodity] < input.Units)) continue;
                foreach (var input in recipe.Inputs) Consume(station.Id, input.Commodity, input.Units);
                stock[output.Commodity] += output.Units;
                RecordUnits(ledger.Produced, output.Commodity, output.Units);
            }

            var services = 0;
            if (station.Purpose == StationPurpose.Trade || cycle % 4 == 0)
            {
                services += Consume(station.Id, CommodityCatalog.Food, station.Purpose == StationPurpose.Trade ? 5 : 1) * 21;
                services += Consume(station.Id, CommodityCatalog.Water, station.Purpose == StationPurpose.Trade ? 3 : 1) * 9;
            }
            if (cycle % 4 == 0)
                services += Consume(station.Id, CommodityCatalog.Medicine, 1) * 55;
            if (cycle % 6 == 0)
                services += Consume(station.Id, CommodityCatalog.Equipment, 1) * 130;
            ledger.Credits += Math.Min(Math.Max(0, EconomyRules.TreasuryLimit - ledger.Credits), services);
        }
    }

    private int Consume(int stationId, ushort commodity, int requested)
    {
        var stock = _stationStocks[stationId];
        var used = Math.Min(requested, stock[commodity]);
        if (used <= 0) return 0;
        stock[commodity] -= used;
        RecordUnits(_stationLedgers[stationId].Consumed, commodity, used);
        return used;
    }

    private static void RecordUnits(Dictionary<ushort, int> totals, ushort commodity, int units) =>
        totals[commodity] = totals.GetValueOrDefault(commodity) + units;

    private readonly record struct FreightOffer(int Station, ushort Commodity, int Quantity, int Cost, double Score);

    private FreightOffer? BestFreightOffer(ShipState ship, int origin)
    {
        var work = ship.Work!;
        FreightOffer? best = null;
        foreach (var good in CommodityCatalog.All)
        {
            var ask = Quote(origin, good);
            var supply = Math.Min(ship.Hull.NpcLoad, Math.Min(ask.StationStock - EconomyRules.ExportReserve(good),
                work.TradeCredits / ask.BuyPrice));
            if (supply <= 0) continue;
            foreach (var station in Catalog.Stations)
            {
                if (!CanFreightTo(work, origin, station)) continue;
                var bid = Quote(station.Id, good);
                var quantity = Math.Min(supply, bid.Demand);
                var margin = bid.SellPrice - ask.BuyPrice;
                if (quantity <= 0 || margin <= 0) continue;
                var distance = Vec3d.Distance(Catalog.Find(origin)!.Position, station.Position) / 149_597_870_700d;
                var danger = Catalog.RaiderTerritories.Any(territory => territory.StationId == station.Id);
                var urgency = DeliveryPriority(station, good, bid.StationStock);
                var score = quantity * margin * urgency / (1 + .018 * distance + (danger ? .38 : .0));
                if (best is null || score > best.Value.Score)
                    best = new(station.Id, good.Id, quantity, ask.BuyPrice * quantity, score);
            }
        }
        return best;
    }

    private static double DeliveryPriority(StaticBody station, Commodity good, int stock)
    {
        var essential = good.Id is CommodityCatalog.Food or CommodityCatalog.Water;
        foreach (var recipe in EconomyRules.Recipes(station.Purpose))
            essential |= recipe.Inputs.Any(input => input.Commodity == good.Id);
        var deficit = Math.Max(0, 1 - (double)stock / good.InitialStock);
        return 1 + deficit * (essential ? 5 : 1.4);
    }

    private bool CanFreightTo(NpcWork work, int origin, StaticBody destination) =>
        destination.Id != origin && (work.Security.RouteAvoidanceUntil <= TimeSeconds || destination.Purpose == StationPurpose.Trade);

    private void LoadFreight(ShipState ship, StaticBody station)
    {
        var work = ship.Work!;
        work.HomeStationId = station.Id;
        var ledger = _stationLedgers[station.Id];
        if (work.TradeCredits < 500 && ledger.Credits >= 2500)
        {
            ledger.Credits -= 500;
            work.TradeCredits += 500;
        }
        if (BestFreightOffer(ship, station.Id) is not { } offer)
        {
            if (TimeSeconds >= work.EmptyTransferAvailableAt)
            {
                var reposition = Catalog.Stations.Where(other => CanFreightTo(work, station.Id, other))
                    .Select(other => (Station: other, Offer: BestFreightOffer(ship, other.Id)))
                    .Where(option => option.Offer.HasValue)
                    .OrderByDescending(option => option.Offer!.Value.Score /
                        (1 + Vec3d.Distance(station.Position, option.Station.Position) / 6e12))
                    .FirstOrDefault();
                if (reposition.Station is { } pickup)
                {
                    work.EmptyTransferAvailableAt = TimeSeconds + 210;
                    Journey(ship, pickup.Id, HoldingPoint(ship, pickup));
                    return;
                }
            }
            WaitOutside(ship, 18 + ship.Id % 17);
            return;
        }
        work.CommodityId = offer.Commodity;
        work.Cargo = offer.Quantity;
        work.TradeCredits -= offer.Cost;
        _stationStocks[station.Id][offer.Commodity] -= offer.Quantity;
        _stationLedgers[station.Id].Credits += offer.Cost;
        var destination = Catalog.Find(offer.Station)!;
        Journey(ship, destination.Id, HoldingPoint(ship, destination));
    }

    private int UnloadFreight(ShipState ship, StaticBody station)
    {
        var work = ship.Work!;
        var good = CommodityCatalog.Find(work.CommodityId)!;
        var quote = Quote(station.Id, good);
        var deposited = Math.Min(work.Cargo, quote.Demand);
        if (deposited > 0)
        {
            var settlement = deposited * quote.SellPrice;
            _stationLedgers[station.Id].Credits -= settlement;
            work.TradeCredits += settlement;
            _stationStocks[station.Id][good.Id] += deposited;
            work.Cargo -= deposited;
        }
        return deposited;
    }

    private bool RerouteCargo(ShipState ship, StaticBody station)
    {
        var work = ship.Work!;
        var good = CommodityCatalog.Find(work.CommodityId)!;
        var offer = Catalog.Stations.Where(destination => CanFreightTo(work, station.Id, destination))
            .Select(destination => (Station: destination, Bid: Quote(destination.Id, good)))
            .Where(option => option.Bid.Demand > 0)
            .OrderByDescending(option => Math.Min(option.Bid.Demand, work.Cargo) * option.Bid.SellPrice)
            .FirstOrDefault();
        if (offer.Station is not { } destination) return false;
        Journey(ship, destination.Id, HoldingPoint(ship, destination));
        return true;
    }
}
