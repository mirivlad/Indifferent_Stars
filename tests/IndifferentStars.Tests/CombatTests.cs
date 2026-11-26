using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Tests;

public sealed class CombatTests
{
    [Theory]
    [InlineData(WeaponKind.PulseLaser)]
    [InlineData(WeaponKind.BeamLaser)]
    [InlineData(WeaponKind.Cannon)]
    [InlineData(WeaponKind.Missile)]
    [InlineData(WeaponKind.Turret)]
    public void EveryWeaponDamagesTheActualShipAndUsesItsOwnResource(WeaponKind kind)
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var definition = HullCatalog.All.First(hull => hull.DefaultWeapons.Contains(kind));
        var shooter = world.AddPlayer("Shooter", definition.Id);
        var target = world.AddPlayer("Target");
        shooter.Position = new Vec3d(0, 3_000_000, 0);
        shooter.SetAngles(0, 0);
        target.Position = shooter.Position - new Vec3d(0, 0, 420);
        target.SetAngles(Math.PI, 0);
        var slot = (byte)Array.IndexOf(definition.DefaultWeapons, kind);
        Assert.True(world.ApplyCombatCommand(shooter.Id, new CombatCommandMessage(1, CombatCommand.SelectWeapon, Slot: slot)));
        Assert.True(world.ApplyCombatCommand(shooter.Id, new CombatCommandMessage(2, CombatCommand.Lock, target.Id)));
        var before = target.Combat.Shield;
        var state = shooter.Combat.Slots[slot];
        var loaded = state.Loaded;

        world.ApplyInput(shooter.Id, InputState.None with { Fire = true });
        world.Tick(.05);
        Assert.True(shooter.Combat.Energy < definition.ReactorOutput);
        if (WeaponCatalog.Get(kind).Magazine > 0) Assert.Equal(loaded - 1, state.Loaded);
        if (WeaponCatalog.Get(kind).ProjectileSpeed > 0) Assert.Equal(before, target.Combat.Shield);
        else Assert.True(target.Combat.Shield < before);
        world.ApplyInput(shooter.Id, InputState.None);
        for (var i = 0; i < 20 && target.Combat.Shield == before; i++) world.Tick(.05);

        Assert.True(target.Combat.Shield < before);
        var penetrated = Math.Max(0, WeaponCatalog.Get(kind).Damage - before);
        Assert.Equal(target.Hull.HullIntegrity - penetrated * .28, target.Combat.Hull, 8);
        var events = new List<CombatEventMessage>();
        while (world.TryTakeCombatEvent(out var effect)) events.Add(effect);
        Assert.Contains(events, effect => effect is { Kind: CombatEventKind.Fired } && effect.Weapon == kind);
        Assert.Contains(events, effect => effect is { Kind: CombatEventKind.ShieldHit } && effect.TargetId == target.Id);
    }

    [Fact]
    public void StationSurfaceStopsAShotBeforeItsTarget()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var shooter = world.AddPlayer("Shooter");
        var target = world.AddPlayer("Target");
        var station = world.Catalog.Station;
        shooter.Position = station.Position + new Vec3d(0, 2400, 0);
        target.Position = station.Position - new Vec3d(0, 55, 0);
        shooter.SetAngles(0, -Math.PI * .5);
        var high = shooter.Position - Vec3d.Up * (shooter.Hull.Radius + .15);
        var pose = WorldSurfaces.Body(world.Catalog, station, 0);
        Assert.NotNull(SurfaceQuery.Raycast(pose.Mesh, pose.Local(high), pose.Local(target.Position)));
        world.ApplyInput(shooter.Id, InputState.None with { Fire = true });

        world.Tick(.05);

        Assert.Equal(target.Hull.HullIntegrity * .55, target.Combat.Shield);
        Assert.True(world.TryTakeCombatEvent(out var shot));
        Assert.Equal(CombatEventKind.Fired, shot.Kind);
        Assert.Equal(0u, shot.TargetId);
        Assert.True(Vec3d.Distance(shot.From, shot.To) < Vec3d.Distance(shot.From, target.Position));
    }

    [Fact]
    public void FiniteProjectileHitsTheHullCrossingItsPathBetweenTicks()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var definition = HullCatalog.All.First(hull => hull.DefaultWeapons.Contains(WeaponKind.Cannon));
        var shooter = world.AddPlayer("Shooter", definition.Id);
        var target = world.AddPlayer("Crossing");
        shooter.Position = new Vec3d(0, 3_000_000, 0);
        shooter.SetAngles(0, 0);
        target.Position = shooter.Position + new Vec3d(-24, 0, -143);
        target.SetAngles(Math.PI, 0);
        var slot = (byte)Array.IndexOf(definition.DefaultWeapons, WeaponKind.Cannon);
        Assert.True(world.ApplyCombatCommand(shooter.Id, new CombatCommandMessage(1, CombatCommand.SelectWeapon, Slot: slot)));
        world.ApplyInput(shooter.Id, InputState.None with { Fire = true });
        world.Tick(.05);
        var cannon = Assert.Single(world.CaptureSimulation().Projectiles!);
        var end = cannon.Position + cannon.Direction * (WeaponCatalog.Get(cannon.Kind).ProjectileSpeed * .05);
        target.Velocity = new Vec3d(700, 0, 0);
        target.Position = Vec3d.Lerp(cannon.Position, end, .5) - target.Velocity * .025;
        var startPose = WorldSurfaces.Ship(target, target.Position, target.Orientation);
        Assert.Null(SurfaceQuery.Raycast(startPose.Mesh, startPose.Local(cannon.Position), startPose.Local(end)));
        world.ApplyInput(shooter.Id, InputState.None);
        world.ApplyInput(target.Id, InputState.None with { FlightAssist = false });

        world.Tick(.05);

        var endPose = WorldSurfaces.Ship(target, target.Position, target.Orientation);
        Assert.Null(SurfaceQuery.Raycast(endPose.Mesh, endPose.Local(cannon.Position), endPose.Local(end)));
        Assert.Equal(target.Hull.HullIntegrity * .55 - WeaponCatalog.Get(cannon.Kind).Damage, target.Combat.Shield, 8);
    }

    [Fact]
    public void ABlastLeavesCargoOnlyInTheWreckAndRecoveryPreservesTheAccount()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var shooter = world.AddPlayer("Shooter");
        var target = world.AddPlayer("Target", HullCatalog.Find("tern")!.Id);
        target.Docked = true;
        Assert.True(world.Trade(target.Id, 1, 2, true).Success);
        var credits = world.GetMarket(target.Id)!.Credits;
        target.Docked = false;
        shooter.Position = new Vec3d(0, 3_000_000, 0);
        shooter.SetAngles(0, 0);
        target.Position = shooter.Position - new Vec3d(0, 0, 280);
        target.SetAngles(Math.PI, 0);
        target.Combat.Shield = target.Combat.Armor = 0;
        target.Combat.Hull = 1;

        world.ApplyInput(shooter.Id, InputState.None with { Fire = true });
        world.Tick(.05);
        world.ApplyInput(shooter.Id, InputState.None);

        Assert.True(target.Combat.Destroyed);
        Assert.Equal(0, world.GetMarket(target.Id)!.CargoUsed);
        Assert.Equal(2, Assert.Single(world.Wrecks).Cargo[(ushort)1]);
        Assert.Equal(2, world.CaptureSimulation().Economy!.CargoLost[(ushort)1]);
        Assert.Equal(world.Wrecks[0].Position, Assert.Single(world.BuildWreckContacts(shooter).Contacts).Position);
        Assert.False(world.ApplyCombatCommand(target.Id, new CombatCommandMessage(2, CombatCommand.Recover)));
        for (var i = 0; i < 162; i++) world.Tick(.05);
        Assert.True(world.ApplyCombatCommand(target.Id, new CombatCommandMessage(3, CombatCommand.Recover)));
        Assert.False(target.Combat.Destroyed);
        Assert.Equal(HullCatalog.Courier, target.HullId);
        Assert.Equal(credits - 250, world.GetMarket(target.Id)!.Credits);
        Assert.Equal(2, Assert.Single(world.Wrecks).Cargo[(ushort)1]);
        Assert.False(world.ApplyCombatCommand(target.Id, new CombatCommandMessage(3, CombatCommand.Recover)));
    }

    [Fact]
    public void RepairsStationRestoreSystemsAndSpendCreditsOnce()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var ship = world.AddPlayer("Pilot");
        ship.Combat.Hull -= 20;
        ship.Combat.Armor -= 5;
        ship.Combat.Engines = .40;
        var request = new CombatCommandMessage(1, CombatCommand.Repair);
        Assert.False(world.ApplyCombatCommand(ship.Id, request));
        ship.Docked = true;
        Assert.True(world.ApplyCombatCommand(ship.Id, request));
        Assert.Equal(ship.Hull.HullIntegrity, ship.Combat.Hull);
        Assert.Equal(1, ship.Combat.Engines);
        var credits = world.GetMarket(ship.Id)!.Credits;
        Assert.Equal(CommodityCatalog.StartingCredits - 75, credits);
        Assert.True(world.ApplyCombatCommand(ship.Id, request));
        Assert.Equal(credits, world.GetMarket(ship.Id)!.Credits);
    }
}
