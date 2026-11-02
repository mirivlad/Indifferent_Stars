using System;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

public sealed partial class ShipTrackChecks : Node
{
    private static EntitySnapshot Snapshot(double x, bool docked = false) => new(
        10000, EntityKind.Player, "Test", new Vec3d(x, 0, 0), 0, 0,
        new Vec3d(100, 0, 0), 0, System.Numerics.Quaternion.Identity, Docked: docked);

    public override async void _Ready()
    {
        try
        {
            var track = new ShipTrack();
            track.Reset(Vec3d.Zero, 0, 0);
            track.Push(Snapshot(0), 10, 500_000);
            track.Push(Snapshot(5), 10.1, 500_050);
            track.Push(Snapshot(10), 10.1, 500_100);
            track.Sample(10.075, Vec3d.Zero);
            Check(Math.Abs(track.WorldPosition.X - 7.5) < 0.001, "Batched arrivals must retain server sample spacing.");
            track.Push(Snapshot(-100, true), 10.2, 500_050);
            track.Push(Snapshot(-200, true), 10.2, 500_100);
            Check(!track.Docked && track.LatestPosition.X == 10, "Stale/duplicate packets must not roll back state.");
            Check(!track.Push(Snapshot(-100) with { CruiseInhibited = true, CruiseBraking = true }, 10.2, 500_050),
                "A stale cruise refusal must not cancel a newer client request.");
            Check(!track.CruiseInhibited && !track.CruiseBraking, "Stale packets must not replace cruise safety state.");
            track.Push(Snapshot(50_000), 11, 500_150);
            track.Sample(11, new Vec3d(50_000, 0, 0));
            Check(track.RenderPosition.Length() < 0.001, "Warp resets must re-anchor the timeline.");
            track.Reset(Vec3d.Zero, 0, 0);
            track.Push(Snapshot(2), 20, 1);
            track.Sample(20, Vec3d.Zero);
            Check(track.WorldPosition.X == 2, "Reconnect must accept a restarted server clock.");
            var worldRoot = new Node3D();
            AddChild(worldRoot);
            var builder = new WorldBuilder(worldRoot, WorldCatalog.Generate(20261101), false);
            builder.Build();
            builder.Process(0, new Vec3d(100_000, 0, 0), Vec3d.Zero);
            Check(builder.LoadedAsteroidSectors == 0, "Distant asteroid sectors must unload.");
            builder.Process(0, new Vec3d(0, 0, 35_000), Vec3d.Zero);
            Check(builder.LoadedAsteroidSectors == 1, "Only the nearby belt should load.");
            builder.Process(0, Vec3d.Zero, Vec3d.Zero);
            Check(builder.LoadedAsteroidSectors == 2, "Returning must reload nearby sectors.");
            var catalog = WorldCatalog.Generate(20261101);
            var celestial = new CelestialRenderer(worldRoot, catalog, false);
            var previousAngularSize = 0.0;
            foreach (var altitude in new[] { 1_400_000.0, 500_000.0, 104_000.0, 99_000.0 })
            {
                var observer = new Vec3d(0, altitude - WorldCatalog.StationOrbitAltitude, 0);
                var origin = RenderFrame.ChooseOrigin(observer, Vec3d.Zero);
                celestial.Update(observer, origin, 0);
                var displayOffset = celestial.PlanetHolder.Position - ShipTrack.ToVector3(observer - origin);
                var angularSize = celestial.PlanetHolder.Scale.X / displayOffset.Length();
                var expected = catalog.Planet.Radius / (catalog.Planet.Radius + altitude);
                Check(Math.Abs(angularSize - expected) < 0.00001, "Planet angular size must follow actual observer distance.");
                Check(angularSize > previousAngularSize, "Planet must grow during approach, including near the old projection cap.");
                previousAngularSize = angularSize;
            }
            worldRoot.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("ShipTrack checks passed: batched arrivals, stale packets, warp, reconnect.");
            GD.Print("Streaming checks passed: unload, nearby belt, return.");
            GD.Print("Planet approach checks passed: angular growth and true-scale transition.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
