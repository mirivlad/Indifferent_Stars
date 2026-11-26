using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public sealed record WorldWreck(ulong Id, string Name, Vec3d Position, double ExpiresAt, Dictionary<ushort, int> Cargo);

public sealed partial class GameWorld
{
    private readonly List<FlightProjectile> _projectiles = new();
    private readonly List<WorldWreck> _wrecks = new();
    private readonly Queue<CombatEventMessage> _combatEvents = new();
    private ulong _combatSequence;

    public IReadOnlyList<WorldWreck> Wrecks => _wrecks;
    public bool TryTakeCombatEvent(out CombatEventMessage combatEvent) => _combatEvents.TryDequeue(out combatEvent!);

    public WreckContactsMessage BuildWreckContacts(ShipState observer)
    {
        var range = observer.Hull.TacticalRange * observer.Combat.Sensors;
        return new WreckContactsMessage(TickCount, _wrecks
            .Select(wreck => (Wreck: wreck, Range: Vec3d.Distance(wreck.Position, observer.Position)))
            .Where(contact => contact.Range <= range)
            .OrderBy(contact => contact.Range).Take(WreckContactsMessage.MaxContacts)
            .Select(contact => new WreckContact(contact.Wreck.Id, contact.Wreck.Position)).ToArray());
    }

    public CombatStatusMessage? BuildCombatStatus(uint id)
    {
        if (GetPlayer(id) is not { } ship) return null;
        var state = ship.Combat;
        return new(state.TargetId, state.Order, state.SelectedSlot,
            (float)(state.Energy / ship.Hull.ReactorOutput), (float)(state.Shield / (ship.Hull.HullIntegrity * .55)),
            (float)(state.Armor / (ship.Hull.HullIntegrity * .4)), (float)(state.Hull / ship.Hull.HullIntegrity),
            (float)state.Engines, (float)state.Sensors, (float)state.Weapons, state.Destroyed,
            (float)Math.Max(0, state.RecoverAt - TimeSeconds),
            state.Slots.Select(slot => new WeaponStatus(slot.Kind, (ushort)slot.Loaded, (ushort)slot.Reserve,
                (float)Math.Max(0, Math.Max(slot.NextShot, slot.ReloadAt) - TimeSeconds))).ToArray());
    }

    public bool ApplyCombatCommand(uint id, CombatCommandMessage request)
    {
        if (GetPlayer(id) is not { } ship) return false;
        var state = ship.Combat;
        if (request.Command == CombatCommand.Recover) return RecoverPlayer(ship);
        if (request.Command == CombatCommand.Repair) return RepairPlayer(ship);
        if (state.Destroyed || ship.InWarp) return false;
        if (request.Command == CombatCommand.SelectWeapon)
        {
            if (request.Slot >= state.Slots.Length) return false;
            state.SelectedSlot = request.Slot;
            return true;
        }
        if (request.Command == CombatCommand.Stop)
        {
            state.Order = CombatOrder.None;
            return true;
        }
        if (ship.Docked || CombatTarget(ship, request.TargetId) is not { } target) return false;
        if (request.Command == CombatCommand.Lock)
        {
            state.TargetId = target.Id;
            state.Order = CombatOrder.None;
            return true;
        }
        var order = request.Command switch
        {
            CombatCommand.Attack => CombatOrder.Attack,
            CombatCommand.Escort => CombatOrder.Escort,
            CombatCommand.KeepDistance => CombatOrder.KeepDistance,
            CombatCommand.Retreat => CombatOrder.Retreat,
            _ => CombatOrder.None,
        };
        if (order == CombatOrder.None || (order == CombatOrder.Attack && state.Slots.Length == 0)) return false;
        CollisionSolver.CancelNavigation(ship);
        state.TargetId = target.Id;
        state.Order = order;
        return true;
    }

    private ShipState? CombatTarget(ShipState attacker, uint id)
    {
        if (id == 0 || id == attacker.Id) return null;
        var target = _players.GetValueOrDefault(id) ?? _npcs.Find(ship => ship.Id == id);
        return target is not null && !target.Combat.Destroyed && !target.InWarp &&
            Vec3d.Distance(attacker.Position, target.Position) <= attacker.Hull.TacticalRange * attacker.Combat.Sensors &&
            CanLocate(attacker, target) ? target : null;
    }

    private InputState CombatSteering(ShipState ship, InputState input)
    {
        var state = ship.Combat;
        if (state.Order == CombatOrder.None) return input;
        if (CombatTarget(ship, state.TargetId) is not { } target)
        {
            state.Order = CombatOrder.None;
            return input;
        }
        if (input.Throttle != 0 || input.Steer != 0 || input.Pitch != 0 || input.Vertical != 0 ||
            input.Strafe != 0 || input.Roll != 0 || input.Brake || input.Cruise) return input;

        var away = (ship.Position - target.Position).Normalized();
        if (away.LengthSquared < .5) away = -ship.Forward;
        var range = state.Slots.Length == 0 ? 1100 : WeaponCatalog.Get(state.Slots[state.SelectedSlot].Kind).RangeMetres;
        var distance = Vec3d.Distance(ship.Position, target.Position);
        var goal = target.Position + away * (state.Order switch
        {
            CombatOrder.Retreat => Math.Max(range * 1.35, distance + 1200),
            CombatOrder.Escort => WorldCatalog.FollowDistance,
            CombatOrder.KeepDistance => range * .78,
            _ => range * .57,
        });
        var control = ApproachPilot.Steer(ship, goal, target.Velocity);
        if (state.Order == CombatOrder.Attack)
        {
            var aim = ApproachPilot.Steer(ship, target.Position, target.Velocity);
            control = control with { Steer = aim.Steer, Pitch = aim.Pitch };
        }
        return control with { Fire = input.Fire, FlightAssist = input.FlightAssist };
    }

    private void StepCombat(ShipState[] ships, Dictionary<uint, (Vec3d Position, Quaternion Orientation)> starts,
        HashSet<uint> warping, double dt)
    {
        _wrecks.RemoveAll(wreck => wreck.ExpiresAt <= TimeSeconds);
        for (var i = _projectiles.Count - 1; i >= 0; i--)
        {
            var bolt = _projectiles[i];
            var weapon = WeaponCatalog.Get(bolt.Kind);
            var direction = bolt.Direction;
            if (weapon.TrackingRadiansPerSecond > 0 && CombatShip(bolt.Target) is { } target && !target.InWarp && !target.Combat.Destroyed)
            {
                var desired = (target.Position - bolt.Position).Normalized();
                var angle = Math.Acos(Math.Clamp(Vec3d.Dot(direction, desired), -1, 1));
                var step = weapon.TrackingRadiansPerSecond * dt;
                if (angle <= step) direction = desired;
                else
                {
                    var lateral = (desired - direction * Vec3d.Dot(desired, direction)).Normalized();
                    if (lateral.LengthSquared < .5)
                        lateral = Vec3d.Cross(direction, Math.Abs(direction.Y) > .9 ? new Vec3d(1, 0, 0) : Vec3d.Up).Normalized();
                    direction = direction * Math.Cos(step) + lateral * Math.Sin(step);
                }
            }
            var travel = Math.Min(bolt.RangeRemaining, weapon.ProjectileSpeed * dt);
            var end = bolt.Position + direction * travel;
            if (TraceCombat(ships, bolt.Owner, bolt.Position, end, dt, starts, warping,
                travel / (weapon.ProjectileSpeed * dt)) is { } hit)
            {
                ResolveShotHit(bolt.Owner, bolt.Kind, bolt.Damage, hit);
                _projectiles.RemoveAt(i);
            }
            else if (travel >= bolt.RangeRemaining) _projectiles.RemoveAt(i);
            else
            {
                _projectiles[i] = bolt with { Direction = direction, Position = end, RangeRemaining = bolt.RangeRemaining - travel };
                if (bolt.Kind == WeaponKind.Missile)
                    EmitCombat(bolt.Owner, 0, CombatEventKind.MissileTrail, bolt.Kind, bolt.Position, end);
            }
        }

        foreach (var ship in ships)
        {
            var state = ship.Combat;
            if (state.Destroyed) continue;
            state.Energy = Math.Min(ship.Hull.ReactorOutput, state.Energy + ship.Hull.ReactorOutput * .155 * dt);
            var missingShield = ship.Hull.HullIntegrity * .55 - state.Shield;
            if (missingShield > 0 && TimeSeconds - state.LastDamage > 4 && state.Energy > ship.Hull.ReactorOutput * .25)
            {
                var repair = Math.Min(missingShield, ship.Hull.HullIntegrity * .046 * dt);
                state.Shield += repair;
                state.Energy -= repair * .48;
            }
            foreach (var slot in state.Slots)
            {
                if (slot.ReloadAt == 0 || TimeSeconds < slot.ReloadAt) continue;
                slot.Loaded = Math.Min(WeaponCatalog.Get(slot.Kind).Magazine, slot.Reserve);
                slot.Reserve -= slot.Loaded;
                slot.ReloadAt = 0;
            }
            if (ship.Docked || ship.InWarp || state.Slots.Length == 0) continue;
            var target = CombatTarget(ship, state.TargetId);
            if (target is null)
            {
                state.TargetId = 0;
                state.Order = CombatOrder.None;
            }
            var manual = _inputs.TryGetValue(ship.Id, out var input) && input.AgeSeconds <= InputTimeoutSeconds && input.Input.Fire;
            if (state.Order != CombatOrder.Attack && !manual) continue;
            FireWeapon(ship, target, ships, manual, dt, warping);
        }
    }

    private ShipState? CombatShip(uint id) => _players.GetValueOrDefault(id) ?? _npcs.Find(ship => ship.Id == id);

    private void FireWeapon(ShipState ship, ShipState? target, ShipState[] ships, bool manual, double dt, HashSet<uint> warping)
    {
        var state = ship.Combat;
        var slot = state.Slots[state.SelectedSlot];
        var weapon = WeaponCatalog.Get(slot.Kind);
        if (state.Weapons <= .2 || state.Energy < weapon.Energy || TimeSeconds < slot.NextShot || slot.ReloadAt > 0) return;
        if (weapon.Magazine > 0 && slot.Loaded == 0)
        {
            if (slot.Reserve > 0) slot.ReloadAt = TimeSeconds + weapon.ReloadSeconds;
            return;
        }
        var forward = ship.Forward;
        var direction = forward;
        if (target is not null)
        {
            var offset = target.Position - ship.Position;
            if (!manual && offset.Length > weapon.RangeMetres) return;
            var lead = weapon.ProjectileSpeed > 0 && weapon.TrackingRadiansPerSecond == 0
                ? target.Velocity * Math.Min(1.5, offset.Length / weapon.ProjectileSpeed) : Vec3d.Zero;
            var aimed = (offset + lead).Normalized();
            if (Vec3d.Dot(forward, aimed) >= Math.Cos(weapon.AimConeRadians)) direction = aimed;
            else if (!manual) return;
            else target = null;
        }
        if (slot.Kind == WeaponKind.Missile && target is null) return;
        var random = new SeedStream(Catalog.Seed, (int)ship.Id, $"shot-{_combatSequence}");
        var cross = Vec3d.Cross(direction, Math.Abs(direction.Y) > .9 ? new Vec3d(1, 0, 0) : Vec3d.Up).Normalized();
        var spread = weapon.SpreadRadians * (2 - state.Weapons);
        direction = (direction + cross * random.Between(-spread, spread) +
            Vec3d.Cross(direction, cross) * random.Between(-spread, spread)).Normalized();
        var start = ship.Position + direction * (ship.Hull.Radius + .15);
        state.Energy -= weapon.Energy;
        slot.NextShot = TimeSeconds + weapon.IntervalSeconds / state.Weapons;
        if (weapon.Magazine > 0) slot.Loaded--;
        var damage = weapon.Damage * (.65 + .35 * state.Weapons);
        var end = start + direction * weapon.RangeMetres;
        if (weapon.ProjectileSpeed > 0)
        {
            _projectiles.Add(new(ship.Id, target?.Id ?? 0, slot.Kind, start, direction, weapon.RangeMetres, damage));
            EmitCombat(ship.Id, target?.Id ?? 0, CombatEventKind.Fired, slot.Kind, start, start + direction * (weapon.ProjectileSpeed * .12));
            return;
        }
        var hit = TraceCombat(ships, ship.Id, start, end, dt, warping: warping);
        EmitCombat(ship.Id, hit?.Ship?.Id ?? 0, CombatEventKind.Fired, slot.Kind, start, hit?.Point ?? end);
        if (hit is { } impact) ResolveShotHit(ship.Id, slot.Kind, damage, impact);
    }

    private readonly record struct CombatHit(Vec3d Point, ShipState? Ship);

    private CombatHit? TraceCombat(ShipState[] ships, uint owner, Vec3d from, Vec3d to, double dt,
        Dictionary<uint, (Vec3d Position, Quaternion Orientation)>? starts = null,
        HashSet<uint>? warping = null, double tickFraction = 1)
    {
        var first = 1.0;
        CombatHit? result = null;
        foreach (var ship in ships)
        {
            if (ship.Id == owner || ship.Combat.Destroyed || warping?.Contains(ship.Id) == true || ship.InWarp) continue;
            var end = WorldSurfaces.Ship(ship, ship.Position, ship.Orientation);
            var begin = end;
            if (starts is not null)
            {
                var before = starts[ship.Id];
                begin = end with { Position = before.Position, Rotation = before.Orientation };
                end = At(begin, end, tickFraction);
            }
            Test(begin, end, ship);
        }
        foreach (var body in CollisionCandidates(from, to, 0))
        {
            var end = BodySurface(body, TimeSeconds + dt * tickFraction);
            Test(starts is null ? end : BodySurface(body, TimeSeconds), end, null);
        }
        foreach (var body in Catalog.Celestials)
            Consider(CollisionSolver.ContactTime(from, to, Catalog.CelestialOffset(body), body.Radius), null);
        return result;

        void Test(SurfacePose begin, SurfacePose end, ShipState? ship)
        {
            var relativeFrom = from - begin.Position;
            var relativeTo = to - end.Position;
            if (relativeFrom.LengthSquared > end.Radius * end.Radius &&
                CollisionSolver.ContactTime(relativeFrom, relativeTo, Vec3d.Zero, end.Radius) is null) return;
            var arc = SurfaceContact.AngularMotion(begin.Rotation, end.Rotation).Length * end.Radius;
            var slices = Math.Max(1, (int)Math.Ceiling(arc / SurfaceContact.RotationTolerance));
            for (var i = 0; i < slices; i++)
            {
                var a = (double)i / slices;
                if (a >= first) break;
                var b = Math.Min(first, (double)(i + 1) / slices);
                var basis = Quaternion.Conjugate(Quaternion.Slerp(begin.Rotation, end.Rotation, (float)((a + b) * .5)));
                var localFrom = SurfaceMesh.Rotate(Vec3d.Lerp(relativeFrom, relativeTo, a), basis) / end.Scale;
                var localTo = SurfaceMesh.Rotate(Vec3d.Lerp(relativeFrom, relativeTo, b), basis) / end.Scale;
                if (SurfaceQuery.Raycast(end.Mesh, localFrom, localTo) is not { } t) continue;
                Consider(a + (b - a) * t, ship);
                break;
            }
        }

        static SurfacePose At(SurfacePose begin, SurfacePose end, double t) => end with
        {
            Position = Vec3d.Lerp(begin.Position, end.Position, t),
            Rotation = Quaternion.Slerp(begin.Rotation, end.Rotation, (float)t),
        };

        void Consider(double? t, ShipState? ship)
        {
            if (t is not { } contact || contact >= first) return;
            first = contact;
            result = new CombatHit(Vec3d.Lerp(from, to, contact), ship);
        }
    }

    private void ResolveShotHit(uint attacker, WeaponKind weapon, double damage, CombatHit hit)
    {
        if (hit.Ship is null || hit.Ship.Docked)
            EmitCombat(attacker, hit.Ship?.Id ?? 0, CombatEventKind.HullHit, weapon, hit.Point, hit.Point);
        else
            DealCombatDamage(hit.Ship, attacker, weapon, hit.Point, damage);
    }

    private void DealCombatDamage(ShipState ship, uint attacker, WeaponKind weapon, Vec3d point, double damage)
    {
        var state = ship.Combat;
        if (state.Destroyed || damage <= 0) return;
        state.LastDamage = TimeSeconds;
        var shield = Math.Min(state.Shield, damage);
        state.Shield -= shield;
        var penetrated = damage - shield;
        if (shield > 0) EmitCombat(attacker, ship.Id, CombatEventKind.ShieldHit, weapon, point, point);
        if (penetrated <= 0) return;
        var armor = Math.Min(state.Armor, penetrated * .72);
        state.Armor -= armor;
        var hullDamage = penetrated - armor;
        state.Hull = Math.Max(0, state.Hull - hullDamage);
        if (state.Hull < ship.Hull.HullIntegrity * .65)
        {
            var fault = hullDamage / ship.Hull.HullIntegrity * 1.6;
            var longitudinal = Vec3d.Dot(point - ship.Position, ship.Forward);
            if (longitudinal < -ship.Hull.Radius * .16) state.Engines = Math.Max(.25, state.Engines - fault);
            else if (longitudinal > ship.Hull.Radius * .16) state.Sensors = Math.Max(.25, state.Sensors - fault);
            else state.Weapons = Math.Max(.15, state.Weapons - fault);
        }
        EmitCombat(attacker, ship.Id, CombatEventKind.HullHit, weapon, point, point);
        if (state.Hull <= 0) DestroyShip(ship, attacker, weapon, point);
    }

    private void DestroyShip(ShipState ship, uint attacker, WeaponKind weapon, Vec3d point)
    {
        ship.Combat.Destroyed = true;
        ship.Combat.RecoverAt = TimeSeconds + 8;
        ship.Combat.Order = CombatOrder.None;
        ship.TransponderEnabled = false;
        ship.Velocity = Vec3d.Zero;
        ship.AngularVelocity = Vec3d.Zero;
        ship.Throttle = 0;
        ship.Docked = false;
        ship.DockingStationId = 0;
        CollisionSolver.CancelNavigation(ship);
        EmitCombat(attacker, ship.Id, CombatEventKind.Destroyed, weapon, point, point);
        var cargo = new Dictionary<ushort, int>();
        if (_accounts.TryGetValue(ship.Id, out var account))
        {
            foreach (var item in account.Cargo)
                if (item.Value > 0) cargo.Add(item.Key, item.Value);
            account.Cargo.Clear();
        }
        if (ship.Work is { Cargo: > 0 } work)
        {
            cargo[work.CommodityId] = cargo.GetValueOrDefault(work.CommodityId) + work.Cargo;
            work.Cargo = 0;
        }
        _wrecks.Add(new WorldWreck(_combatSequence, ship.Name, ship.Position, TimeSeconds + 600, cargo));
    }

    private bool RecoverPlayer(ShipState ship)
    {
        if (!ship.Combat.Destroyed || TimeSeconds < ship.Combat.RecoverAt) return false;
        var account = _accounts[ship.Id];
        var station = Catalog.Find(account.StationId) ?? Catalog.Station;
        ship.HullId = HullCatalog.Courier;
        ship.Combat = ShipCombat.Create(ship.Hull);
        ship.Position = FindReturnPosition(station, ship);
        ship.SetAngles(FlightModel.AnglesForDirection(station.Position - ship.Position).Yaw, 0);
        ship.TransponderEnabled = true;
        ship.Cruising = false;
        ship.CruiseBraking = false;
        ship.CruiseInhibited = false;
        ship.Impact = null;
        _inputs[ship.Id] = new PlayerInputSlot();
        account.Credits -= Math.Min(250, account.Credits);
        return true;
    }

    private bool RepairPlayer(ShipState ship)
    {
        if (!ship.Docked || ship.Combat.Destroyed) return false;
        var state = ship.Combat;
        var integrity = ship.Hull.HullIntegrity;
        var damage = integrity - state.Hull + integrity * .4 - state.Armor;
        var cost = (int)Math.Ceiling(Math.Max(0, damage) * 3);
        var account = _accounts[ship.Id];
        if (cost > account.Credits) return false;
        account.Credits -= cost;
        state.Hull = integrity;
        state.Armor = integrity * .4;
        state.Engines = state.Sensors = state.Weapons = 1;
        return true;
    }

    private void EmitCombat(uint source, uint target, CombatEventKind kind, WeaponKind weapon, Vec3d from, Vec3d to)
    {
        if (_combatEvents.Count >= 256) _combatEvents.Dequeue();
        _combatEvents.Enqueue(new CombatEventMessage(++_combatSequence, source, target, kind, weapon, from, to));
    }
}
