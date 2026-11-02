using System;
using System.IO;
using System.Linq;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class SurfaceFlightChecks : Node3D
{
    public override async void _Ready()
    {
        try
        {
            CollisionAssets.Validate();
            Locale.Load("ru");
            var output = System.Environment.GetEnvironmentVariable("IS_SURFACE_CAPTURE") ?? "/tmp/indifferent-surfaces";
            Directory.CreateDirectory(output);
            var camera = new Camera3D { Current = true, Fov = 55, Near = 0.35f, Far = 400000 };
            AddChild(camera);
            var stationSize = ModelPose.StationScale / 55;
            foreach (var (x, collision, name, title) in new[]
            {
                (110.0, false, "01_open_ring", "ПРОХОД ЧЕРЕЗ ОТВЕРСТИЕ — СВОБОДЕН"),
                (160.0, true, "02_ring_contact", "ВНЕШНЯЯ ОБОЛОЧКА ОБОДА — КОНТАКТ"),
                (308.0, true, "03_panel_contact", "ВНЕШНЯЯ ОБОЛОЧКА ПАНЕЛИ — КОНТАКТ"),
            })
            {
                var catalog = WorldCatalog.Generate(GameMain.DefaultSeed);
                var world = new GameWorld(catalog);
                var pilot = world.AddPlayer("Surface check");
                var rotation = ModelPose.StationRotation(catalog.Station, 0);
                pilot.Position = SurfaceMesh.Rotate(new Vec3d(x, 40, 0) * stationSize, rotation);
                pilot.Velocity = new Vec3d(0, -1600 * stationSize, 0);
                pilot.Orientation = rotation;
                world.Tick(0.05);
                if (collision) Check(pilot.Position.Y > -30 * stationSize &&
                    (pilot.Velocity - Vec3d.Cross(new Vec3d(0, ModelPose.StationSpin, 0), pilot.Position - catalog.Station.Position)).Length < .001,
                    "The visible metal must stop the hull relative to the rotating surface.");
                else Check(pilot.Position.Y < -39 * stationSize && pilot.Velocity.Y < -1599 * stationSize, "Empty ring space must remain traversable.");
                var root = new Node3D(); AddChild(root);
                var builder = new WorldBuilder(root, catalog, true) { SimulationTime = world.TimeSeconds };
                builder.Build(); builder.Process(0, pilot.Position, Vec3d.Zero);
                camera.Far = builder.RequiredFarPlane;
                var ship = new ShipView(pilot.Id, true, pilot.Name, true);
                root.AddChild(ship);
                ship.Track.Push(world.BuildSnapshot().Single(entity => entity.Id == pilot.Id), Time.GetTicksMsec() / 1000.0, 50);
                ship.UpdateVisual(Vec3d.Zero, 0.05f);
                camera.Position = ShipTrack.ToVector3(SurfaceMesh.Rotate(new Vec3d(x + 25, 140, 170) * stationSize, rotation));
                camera.LookAt(ShipTrack.ToVector3(SurfaceMesh.Rotate(new Vec3d(x, -15, 0) * stationSize, rotation)), Vector3.Up);
                var layer = new CanvasLayer(); root.AddChild(layer);
                var label = new Label { Text = title, Position = new Vector2(30, 25) };
                label.AddThemeFontSizeOverride("font_size", 26);
                layer.AddChild(label);
                await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "Capture required.");
                root.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            GD.Print("SURFACE FLIGHT CHECKS PASSED: authoritative ring passage, rim contact and solar-panel contact.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
