using System;
using System.IO;
using System.Linq;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class EffectsChecks : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var camera = new Camera3D { Current = true, Fov = 70, Far = 2_000_000 };
            AddChild(camera);
            var parent = new Node3D();
            AddChild(parent);
            var trail = new IonTrail();
            parent.AddChild(trail);
            var origin = new Vec3d(1_000_000_000, 0, 0);
            var a = origin + new Vec3d(1, 0, 0);
            var b = origin + new Vec3d(21, 0, 0);
            trail.UpdateTrail(0.05f, origin, a, a, true);
            trail.UpdateTrail(0.05f, origin, b, b, true);
            Check(trail.Mesh.GetSurfaceCount() == 1, "Moving exhaust must create a trail.");
            var vertices = Vertices(trail);
            var before = (vertices[0] + vertices[1]) / 2;
            parent.Rotation = new Vector3(0, Mathf.Pi, 0);
            var shifted = origin + new Vec3d(10_000, 0, 0);
            trail.UpdateTrail(0.01f, shifted, b, b, false);
            vertices = Vertices(trail);
            var after = (vertices[0] + vertices[1]) / 2;
            Check(Mathf.Abs(after.X + 10_000 - before.X) < 0.001f, "Origin shifts must preserve emitted world positions.");
            Check(trail.GlobalBasis.IsEqualApprox(Basis.Identity), "Emitted trails must not turn with the hull.");
            trail.UpdateTrail(0.4f, shifted, b, b, false);
            Check(trail.Mesh.GetSurfaceCount() == 0, "Exhaust must expire quickly after thrust stops.");
            trail.UpdateTrail(0.01f, origin, a, a, true);
            trail.UpdateTrail(0.01f, origin, b, b, true);
            trail.UpdateTrail(0.01f, origin, b + new Vec3d(100_000, 0, 0), b, true);
            Check(trail.Mesh.GetSurfaceCount() == 0, "Warp must not draw a line across the system.");
            parent.QueueFree();

            var lightRoot = new Node3D(); AddChild(lightRoot);
            var lightCatalog = WorldCatalog.Generate(20261101);
            var celestial = new CelestialRenderer(lightRoot, lightCatalog, true);
            foreach(var systemPosition in new[] { lightCatalog.LocalFrameOrigin,
                lightCatalog.Star.SystemPosition + new Vec3d(-2_000_000_000, 500_000_000, 0),
                lightCatalog.Star.SystemPosition + new Vec3d(0, 2_000_000_000, 0) })
            {
                var local = systemPosition - lightCatalog.LocalFrameOrigin;
                celestial.Update(local, local, 0);
                var rays = -lightRoot.GetNode<DirectionalLight3D>("Starlight").GlobalBasis.Z;
                var expected = ShipTrack.ToVector3((systemPosition-lightCatalog.Star.SystemPosition).Normalized());
                Check(rays.Dot(expected) > .999999f,"Sunlight rays must travel from the star to the observer, including above its pole.");
            }
            lightRoot.QueueFree();

            var ship = new ShipView(10000, true, "Effects", true);
            AddChild(ship);
            ship.Track.Push(Snapshot(Vec3d.Zero), -1, 1);
            ship.UpdateVisual(Vec3d.Zero, 0.05f);
            ship.Track.Push(Snapshot(new Vec3d(100, 0, 0)), -0.95, 51);
            ship.UpdateVisual(Vec3d.Zero, 0.05f);
            var jets = ship.FindChildren("ManeuverJet_*", "Node3D", true, false).Cast<Node3D>().ToArray();
            Check(jets.Any(jet => jet.Visible && jet.Position.X < -3), "Lateral acceleration must fire the opposite maneuver ports.");
            ship.Track.Push(Snapshot(new Vec3d(0, 0, -100)), -0.9, 101);
            ship.UpdateVisual(Vec3d.Zero, 0.05f);
            ship.Track.Push(Snapshot(Vec3d.Zero), -0.85, 151);
            ship.UpdateVisual(Vec3d.Zero, 0.05f);
            Check(jets.Any(jet => jet.Visible && jet.Position.Z < -3), "Braking must fire retro-thrusters.");
            ship.Track.Push(Snapshot(Vec3d.Zero) with { Orientation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 0.05f) }, -0.8, 201);
            ship.UpdateVisual(Vec3d.Zero, 0.05f);
            Check(jets.Any(jet => jet.Visible && Mathf.Abs(jet.Position.Y) > 0.2f), "Roll must fire dorsal/ventral maneuver pairs.");
            ship.SetEngineEffects(false);
            Check(jets.All(jet => !jet.Visible), "Engine quality switch must hide maneuver effects.");
            ship.QueueFree();
            var map = new TacticalView { PlayerPosition = new Vec3d(2000, 1500, 35000) };
            AddChild(map);
            var waypoint = new Vec3d(-2000, 1500, 25000);
            Check(Vec3d.Distance(map.WorldPositionAt(map.ScreenPosition(waypoint)), waypoint) < 0.01,
                "Map waypoints must invert projection and preserve the ship's height.");
            map.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("Effects checks passed: world trails, origin shift, hull rotation, expiry, warp reset, maneuver ports, quality switch, stellar light direction and pole.");

            var args = OS.GetCmdlineUserArgs();
            if (args.Length > 0)
            {
                var output = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(output);
                var catalog = WorldCatalog.Generate(20261101);
                var world = new GameWorld(catalog);
                var pilot = world.AddPlayer("Camera");
                var root = new Node3D();
                AddChild(root);
                var builder = new WorldBuilder(root, catalog, true);
                builder.Build();
                builder.Process(0, pilot.Position, Vec3d.Zero);
                var basis = new Basis(new Quaternion(pilot.Orientation.X, pilot.Orientation.Y, pilot.Orientation.Z, pilot.Orientation.W));
                camera.Position = ShipTrack.ToVector3(pilot.Position) + basis * new Vector3(1, 5.4f, 17);
                camera.LookAt(ShipTrack.ToVector3(pilot.Position) - basis.Z * 12, basis.Y);
                camera.Far = builder.RequiredFarPlane;
                var planet = root.GetNode<Node3D>("CelestialBodies/PlanetAsterReach");
                var rings = planet.GetNode<Node3D>("Rings");
                var atmosphere = planet.GetNode<Node3D>("Atmosphere");
                foreach (var variant in new[] { "full", "no_glow", "no_rings", "surface_only" })
                {
                    builder.SetGlowEnabled(variant == "full");
                    rings.Visible = variant is "full" or "no_glow";
                    atmosphere.Visible = variant != "surface_only";
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var path = Path.Combine(output, variant + ".png");
                    Check(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "Capture must be written.");
                    GD.Print($"CAPTURE {path}");
                }
            }
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private static EntitySnapshot Snapshot(Vec3d velocity) => new(10000, EntityKind.Player, "Effects", Vec3d.Zero,
        0, 0, velocity, 0, System.Numerics.Quaternion.Identity);

    private static Vector3[] Vertices(IonTrail trail) => trail.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
