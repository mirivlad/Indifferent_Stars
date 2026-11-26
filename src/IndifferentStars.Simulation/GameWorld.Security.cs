using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record SecurityOffense(uint Suspect, uint Victim, Vec3d Position, double ExpiresAt);

public sealed partial class GameWorld
{
    private const uint FirstRaiderId = 9000;
    private readonly Dictionary<uint, SecurityOffense> _securityOffenses = new();
    private readonly Dictionary<int, int> _raiderSupplies = new();
    private readonly Dictionary<int, double> _replacementOrders = new();

    public static Faction Affiliation(ShipState ship) => ship.Work?.Occupation switch
    {
        NpcOccupation.Patrol => Faction.Security,
        NpcOccupation.Raider => Faction.Pirate,
        NpcOccupation.Courier when ship.Id % 4 == 0 => Faction.Independent,
        null => Faction.Independent,
        _ => Faction.Civilian,
    };

    public FactionStanding Standing(ShipState viewer, ShipState other)
    {
        var viewerFaction = Affiliation(viewer);
        var otherFaction = Affiliation(other);
        if (viewerFaction == Faction.Security && IsSuspect(other.Id) ||
            otherFaction == Faction.Security && IsSuspect(viewer.Id)) return FactionStanding.Hostile;
        return WorldCatalog.DefaultStanding(viewerFaction, otherFaction);
    }

    private bool IsSuspect(uint id) =>
        _securityOffenses.TryGetValue(id, out var offense) && offense.ExpiresAt > TimeSeconds;

    private void SpawnRaiders()
    {
        uint id = FirstRaiderId;
        foreach (var territory in Catalog.RaiderTerritories)
        {
            _raiderSupplies.Add(territory.StationId, 72);
            for (var i = 0; i < 2; i++)
            {
                var hull = HullCatalog.Find(i == 0 ? "shrike" : "scavenger")!;
                var random = new SeedStream(Catalog.Seed, (int)id, $"raider-{Catalog.SystemIndex}");
                var point = territory.PatrolPoint + new Vec3d((i == 0 ? -1 : 1) * 2100, 500 + i * 700, 1400);
                var ship = new ShipState
                {
                    Id = id++, HullId = hull.Id, Name = random.Name(), Role = NpcOccupation.Raider.ToString(),
                    Position = point, TransponderEnabled = false,
                    Work = new NpcWork { Occupation = NpcOccupation.Raider, HomeStationId = territory.StationId,
                        TargetId = territory.StationId, Goal = point, Phase = NpcPhase.Travelling },
                };
                for (var attempt = 0; !WarpSafe(ship, ship.Position, 0); attempt++)
                {
                    if (attempt >= 32) throw new InvalidOperationException("No clear raider spawn near hideout.");
                    ship.Position += Vec3d.Up * 1500;
                }
                ship.Work.Goal = ship.Position;
                _npcs.Add(ship);
            }
        }
    }

    private void StepReplacements()
    {
        if (TickCount % 20 != 0) return;
        foreach (var id in _securityOffenses.Where(item => item.Value.ExpiresAt <= TimeSeconds).Select(item => item.Key).ToArray())
            _securityOffenses.Remove(id);
        foreach (var ship in _npcs)
        {
            if (!ship.Combat.Destroyed || ship.Work is not { } work || work.Security.ReplacementAt > TimeSeconds) continue;
            var station = Catalog.Find(work.HomeStationId)!;
            var pirate = work.Occupation == NpcOccupation.Raider;
            var workshop = pirate ? -station.Id : station.Id;
            if (_replacementOrders.GetValueOrDefault(workshop) > TimeSeconds) continue;
            var cost = pirate ? 12 : 8;
            var stock = _stationStocks[station.Id];
            const ushort parts = 6;
            if ((pirate ? _raiderSupplies.GetValueOrDefault(station.Id) : stock[parts]) < cost) continue;
            var position = pirate ? Catalog.RaiderTerritories.Single(region => region.StationId == station.Id).Hideout
                : HoldingPoint(ship, station);
            position += Vec3d.Up * (500 + ship.Id % 13 * 200);
            if (!WarpSafe(ship, position, TimeSeconds)) continue;
            if (pirate) _raiderSupplies[station.Id] -= cost;
            else stock[parts] -= cost;
            _replacementOrders[workshop] = TimeSeconds + 55;
            var generation = work.Security.Replacements + 1;
            work.Security = new NpcSecurity { Replacements = generation };
            var name = new SeedStream(Catalog.Seed, (int)ship.Id, $"reserve-{Catalog.SystemIndex}-{generation}");
            ship.Name = name.Name();
            ship.Combat = ShipCombat.Create(ship.Hull);
            ship.Position = position;
            ship.Velocity = Vec3d.Zero;
            ship.AngularVelocity = Vec3d.Zero;
            ship.Cruising = ship.CruiseBraking = ship.CruiseInhibited = false;
            ship.Impact = null;
            ship.TransponderEnabled = !pirate;
            ship.SetAngles(0, 0);
            work.Cargo = 0;
            work.Phase = NpcPhase.Travelling;
            work.Goal = position;
            if (!pirate)
            {
                NextAssignment(ship);
                Activity(ship, "reserve", $"Reserve hull launched from {station.Name}.", $"Резервный корабль вышел со станции {station.Name}.");
            }
        }
    }

    private void ReportCombatDamage(ShipState victim, uint attackerId)
    {
        if (CombatShip(attackerId) is not { } attacker || attacker == victim) return;
        if (Affiliation(victim) != Faction.Pirate && Affiliation(attacker) != Faction.Security &&
            (!_securityOffenses.TryGetValue(attackerId, out var current) || current.ExpiresAt - TimeSeconds < 100))
        {
            _securityOffenses[attackerId] = new(attackerId, victim.Id, victim.Position, TimeSeconds + 150);
            Activity(victim, "attack", $"Taking fire from {attacker.Name}.", $"Нахожусь под огнём. Атакует {attacker.Name}.");
        }
        if (victim.Work is not { } work) return;
        var brain = work.Security;
        if (brain.ThreatUntil <= TimeSeconds && Affiliation(victim) != Faction.Pirate)
            Activity(victim, "distress", "Under attack. Requesting nearby patrol assistance.",
                "Под обстрелом. Запрашиваю помощь ближайшего патруля.", true);
        brain.ThreatId = attackerId;
        brain.ThreatUntil = TimeSeconds + 38;
    }

    private bool StepWorkerSecurity(ShipState ship, double dt, HashSet<uint> warping)
    {
        var work = ship.Work!;
        var brain = work.Security;
        var state = ship.Combat;
        var threat = brain.ThreatUntil > TimeSeconds ? CombatTarget(ship, brain.ThreatId) : null;
        var assess = TimeSeconds >= brain.NextDecision;
        if (assess) brain.NextDecision = TimeSeconds + .65 + ship.Id % 5 * .035;

        if (work.Occupation == NpcOccupation.Raider)
        {
            StepRaider(ship, threat, assess, dt);
            return true;
        }
        if (work.Occupation == NpcOccupation.Patrol)
        {
            if (threat is not null && Vec3d.Distance(ship.Position, threat.Position) > 75_000) threat = null;
            if (assess && threat is null)
            {
                threat = _securityOffenses.Values.OrderBy(record => Vec3d.Distance(ship.Position, record.Position))
                    .Where(record => record.ExpiresAt > TimeSeconds && Vec3d.Distance(ship.Position, record.Position) < 70_000)
                    .Select(record => CombatTarget(ship, record.Suspect))
                    .FirstOrDefault(suspect => suspect is not null && Standing(ship, suspect) == FactionStanding.Hostile &&
                        Vec3d.Distance(ship.Position, suspect.Position) <= 75_000);
                if (threat is not null)
                {
                    brain.ThreatId = threat.Id;
                    brain.ThreatUntil = TimeSeconds + 50;
                    Activity(ship, "pursuit", $"Responding to the report. Pursuing {threat.Name}.",
                        $"Принял сообщение. Преследую {threat.Name}.", true);
                }
            }
            if (threat is not null)
            {
                brain.EscortId = 0;
                SetNpcCombat(ship, threat, state.Hull < ship.Hull.HullIntegrity * .34 ? CombatOrder.Retreat : CombatOrder.Attack);
                StepInterception(ship, dt);
                return true;
            }
            var escort = brain.EscortUntil > TimeSeconds ? CombatTarget(ship, brain.EscortId) : null;
            if (escort is null && brain.EscortId == 0 && assess && ship.Id % 2 == 0 && brain.EscortAvailableAt <= TimeSeconds)
            {
                escort = _npcs.Where(candidate => candidate.Work?.Occupation == NpcOccupation.Courier &&
                        candidate.RouteActive && Vec3d.Distance(ship.Position, candidate.Position) < 9000 &&
                        Catalog.SecurityAt(candidate.Position) != SecurityZone.Trade && CombatTarget(ship, candidate.Id) is not null)
                    .MinBy(candidate => Vec3d.Distance(ship.Position, candidate.Position));
                if (escort is not null)
                {
                    brain.EscortId = escort.Id;
                    brain.EscortUntil = TimeSeconds + 45;
                    brain.EscortAvailableAt = TimeSeconds + 90;
                    Activity(ship, "escort", $"Escorting {escort.Name} through the departure corridor.",
                        $"Сопровождаю корабль {escort.Name} в коридоре отхода.");
                }
            }
            if (escort is { Docked: false })
            {
                SetNpcCombat(ship, escort, CombatOrder.Escort);
                StepInterception(ship, dt);
                return true;
            }
            brain.EscortId = 0;
            if (brain.InterruptedAssignment)
            {
                brain.InterruptedAssignment = false;
                state.Order = CombatOrder.None;
                NextAssignment(ship);
            }
            return false;
        }
        if (assess && threat is null && brain.NextContactReport <= TimeSeconds)
        {
            var unknown = _npcs.Any(candidate => candidate.Work?.Occupation == NpcOccupation.Raider &&
                Vec3d.Distance(ship.Position, candidate.Position) < 5000 && CombatTarget(ship, candidate.Id) is not null);
            if (unknown)
            {
                brain.NextContactReport = TimeSeconds + 75;
                brain.RouteAvoidanceUntil = TimeSeconds + 180;
                Activity(ship, "hostile_contact", $"Detected a silent ship near {Catalog.SystemName}.",
                    $"Обнаружен корабль без транспондера в системе {Catalog.SystemName}.");
            }
        }
        if (threat is not null)
        {
            brain.RouteAvoidanceUntil = TimeSeconds + 180;
            var distance = Vec3d.Distance(ship.Position, threat.Position);
            if (assess && distance is > 2_900 and < 85_000)
            {
                var shelter = Catalog.Stations.Where(station => station.Purpose == StationPurpose.Trade)
                    .MinBy(station => Vec3d.Distance(ship.Position, station.Position)) ?? Catalog.Station;
                var destination = HoldingPoint(ship, shelter) + Vec3d.Up * WorldCatalog.CruiseClearance;
                if (WarpShip(ship, 0, destination).Success)
                {
                    brain.InterruptedAssignment = true;
                    work.TargetId = shelter.Id;
                    work.Goal = HoldingPoint(ship, shelter);
                    warping.Add(ship.Id);
                    Activity(ship, "escape", $"Escaping to {shelter.Name}.", $"Ухожу к станции {shelter.Name}.", true);
                    return true;
                }
            }
            SetNpcCombat(ship, threat, distance < 1600 && state.Hull > ship.Hull.HullIntegrity * .7
                ? CombatOrder.Attack : CombatOrder.Retreat);
            StepInterception(ship, dt);
            return true;
        }
        if (brain.InterruptedAssignment)
        {
            brain.InterruptedAssignment = false;
            state.Order = CombatOrder.None;
            var shelter = Catalog.Stations.Where(station => station.Purpose == StationPurpose.Trade)
                .MinBy(station => Vec3d.Distance(ship.Position, station.Position)) ?? Catalog.Station;
            Journey(ship, shelter.Id, HoldingPoint(ship, shelter));
        }
        return false;
    }

    private void StepRaider(ShipState ship, ShipState? threat, bool assess, double dt)
    {
        var work = ship.Work!;
        var brain = work.Security;
        var state = ship.Combat;
        var territory = Catalog.RaiderTerritories.Single(region => region.StationId == work.HomeStationId);
        if (state.Hull < ship.Hull.HullIntegrity * .58 || brain.Loot.Values.Sum() >= ship.Hull.NpcLoad)
            brain.Returning = true;
        if (!brain.Returning && assess && _npcs.Count(candidate => candidate.Work?.Occupation == NpcOccupation.Patrol &&
            !candidate.Combat.Destroyed && !candidate.InWarp && Vec3d.Distance(ship.Position, candidate.Position) < 12_000 &&
            CanLocate(ship, candidate)) >= 2)
            brain.Returning = true;
        if (!brain.Returning && threat is null && TryRaiderSalvage(ship, assess))
        {
            state.Order = CombatOrder.None;
            state.TargetId = 0;
            FlightModel.StepControlled(ship, StationClearance(ship) ?? ApproachPilot.Steer(ship, work.Goal), dt);
            return;
        }
        if (assess && !brain.Returning && threat is null)
        {
            threat = RaiderTarget(ship, territory);
            if (threat is not null)
            {
                brain.ThreatId = threat.Id;
                brain.ThreatUntil = TimeSeconds + 32;
            }
        }
        if (threat is not null && (Catalog.SecurityAt(threat.Position) == SecurityZone.Trade ||
            Vec3d.Distance(threat.Position, territory.PatrolPoint) > 65_000))
        {
            brain.ThreatId = 0;
            brain.Returning = true;
        }
        if (!brain.Returning && threat is not null)
        {
            SetNpcCombat(ship, threat, CombatOrder.Attack);
            StepInterception(ship, dt);
            return;
        }

        state.Order = CombatOrder.None;
        state.TargetId = 0;
        if (brain.Returning)
        {
            if (ApproachPilot.Arrived(ship, territory.Hideout))
            {
                ServiceRaider(ship);
                brain.Returning = false;
                brain.ThreatId = 0;
                work.Goal = territory.PatrolPoint;
            }
            else work.Goal = territory.Hideout;
        }
        else if (ApproachPilot.Arrived(ship, work.Goal))
        {
            work.CompletedJobs++;
            var angle = (work.CompletedJobs + ship.Id) * 2.4;
            work.Goal = territory.PatrolPoint + new Vec3d(Math.Cos(angle) * 3300, ship.Id % 3 * 600, Math.Sin(angle) * 3300);
        }
        FlightModel.StepControlled(ship, StationClearance(ship) ??
            ApproachPilot.Steer(ship, work.Goal, maxSpeed: ship.Hull.MaxSpeed), dt);
    }

    private ShipState? RaiderTarget(ShipState raider, RaiderTerritory territory)
    {
        var range = Math.Min(35_000, raider.Hull.TacticalRange * raider.Combat.Sensors);
        var nearby = _npcs.Concat(_players.Values)
            .Where(ship => !ship.Docked && !ship.InWarp && !ship.Combat.Destroyed &&
                Vec3d.Distance(raider.Position, ship.Position) <= range && CanLocate(raider, ship))
            .ToArray();
        if (nearby.Count(ship => Affiliation(ship) == Faction.Security) >= 2) return null;
        return nearby.Where(ship => (Affiliation(ship) is Faction.Civilian or Faction.Independent) &&
                Vec3d.Distance(territory.PatrolPoint, ship.Position) <= 50_000 && Catalog.SecurityAt(ship.Position) != SecurityZone.Trade &&
                ship.Hull.CargoCapacity >= 30 && ship.Velocity.Length < 550)
            .MaxBy(ship => ship.Hull.CargoCapacity * 230 - ship.Hull.HullIntegrity * 30 -
                Vec3d.Distance(raider.Position, ship.Position));
    }

    private static void SetNpcCombat(ShipState ship, ShipState target, CombatOrder order)
    {
        if (ship.Combat.Order != order)
        {
            CollisionSolver.CancelNavigation(ship);
            ship.Work!.Security.InterruptedAssignment = true;
        }
        ship.Combat.TargetId = target.Id;
        ship.Combat.Order = order;
    }

    private void StepInterception(ShipState ship, double dt)
    {
        ship.CruiseSpeedLimit = CruiseLimitAt(ship);
        var control = ship.Velocity.Length > ship.Hull.BoostSpeed
            ? InputState.None with { Brake = true }
            : StationClearance(ship) ?? CombatSteering(ship, InputState.None);
        FlightModel.StepControlled(ship, control, dt);
    }

    private InputState? StationClearance(ShipState ship)
    {
        const double clearance = 540;
        var future = ship.Position + ship.Velocity * 2;
        foreach (var station in Catalog.Stations)
        {
            if (Vec3d.Distance(ship.Position, station.Position) > station.Radius + clearance + ship.Velocity.Length * 2) continue;
            var surface = BodySurface(station, TimeSeconds);
            if (surface.Distance(ship.Position) > clearance && surface.Distance(future) > clearance) continue;
            var away = (ship.Position - station.Position).Normalized();
            return ApproachPilot.Steer(ship, ship.Position + (away + Vec3d.Up * .35) * 1900);
        }
        return null;
    }

    private bool TryRaiderSalvage(ShipState ship, bool assess)
    {
        var brain = ship.Work!.Security;
        var range = Math.Min(18_000, ship.Hull.TacticalRange * ship.Combat.Sensors);
        if (assess && brain.SalvageTarget == 0)
            brain.SalvageTarget = _wrecks.Where(wreck => wreck.Cargo.Count != 0 && Vec3d.Distance(wreck.Position, ship.Position) <= range)
                .MinBy(wreck => Vec3d.Distance(wreck.Position, ship.Position))?.Id ?? 0;
        var target = brain.SalvageTarget == 0 ? null : _wrecks.Find(wreck => wreck.Id == brain.SalvageTarget);
        if (target is null || target.Cargo.Count == 0)
        {
            brain.SalvageTarget = 0;
            return false;
        }
        ship.Work.Goal = target.Position;
        if (Vec3d.Distance(ship.Position, target.Position) <= 40 && ship.Velocity.Length < 6)
        {
            var remaining = ship.Hull.CargoCapacity - brain.Loot.Values.Sum();
            var took = 0;
            foreach (var (commodity, quantity) in target.Cargo.ToArray())
            {
                var count = Math.Min(remaining, quantity);
                if (count <= 0) break;
                took += count;
                remaining -= count;
                brain.Loot[commodity] = brain.Loot.GetValueOrDefault(commodity) + count;
                if (count == quantity) target.Cargo.Remove(commodity);
                else target.Cargo[commodity] -= count;
            }
            if (took > 0) brain.Returning = true;
            brain.SalvageTarget = 0;
        }
        return true;
    }

    private void ServiceRaider(ShipState ship)
    {
        var brain = ship.Work!.Security;
        var stationId = ship.Work.HomeStationId;
        var haul = brain.Loot.Values.Sum();
        if (haul > 0)
            _raiderSupplies[stationId] = Math.Min(150, _raiderSupplies.GetValueOrDefault(stationId) + haul);
        var supplies = Math.Min(_raiderSupplies.GetValueOrDefault(stationId), 10);
        if (supplies <= 0) return;
        var health = Math.Min(1, supplies / (ship.Hull.HullIntegrity * .16));
        ship.Combat.Hull = Math.Min(ship.Hull.HullIntegrity, ship.Combat.Hull + ship.Hull.HullIntegrity * health);
        ship.Combat.Armor = Math.Min(ship.Hull.HullIntegrity * .4, ship.Combat.Armor + ship.Hull.HullIntegrity * .4 * health);
        ship.Combat.Engines = Math.Min(1, ship.Combat.Engines + health);
        ship.Combat.Sensors = Math.Min(1, ship.Combat.Sensors + health);
        ship.Combat.Weapons = Math.Min(1, ship.Combat.Weapons + health);
        foreach (var slot in ship.Combat.Slots)
            slot.Reserve = Math.Min(WeaponCatalog.Get(slot.Kind).SpareRounds, slot.Reserve + supplies);
        _raiderSupplies[stationId] -= supplies;
        brain.Loot.Clear();
    }

    private int StationPricePremium(int stationId, ushort commodityId) => Catalog.Find(stationId)?.Purpose switch
    {
        StationPurpose.Research when commodityId == 5 => 20,
        StationPurpose.Mining when commodityId == 3 => 36,
        StationPurpose.Freight when commodityId == 4 => 19,
        _ => 0,
    };
}
