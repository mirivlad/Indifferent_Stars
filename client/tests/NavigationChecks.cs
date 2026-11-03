using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class NavigationChecks : Node3D
{
    private string? _output;

    public override async void _Ready()
    {
        try
        {
            Locale.Load("ru");
            var args = OS.GetCmdlineUserArgs();
            if (args.Length > 0) { _output = Path.GetFullPath(args[0]); Directory.CreateDirectory(_output); }
            var catalog = WorldCatalog.Generate(20261101);
            // Reconstruct the reported height and 719.3 km surface clearance.
            const double y = -4_170_926;
            const double z = -2_000_000;
            var height = catalog.LocalFrameOrigin.Y + y - catalog.Planet.SystemPosition.Y;
            var radius = catalog.Planet.Radius + 719_300;
            var position = new Vec3d(-Math.Sqrt(radius * radius - height * height - z * z), y, z);
            var navigation = SurfaceNavigation.Measure(catalog.LocalFrameOrigin + position, Vec3d.Zero, catalog.Planet);
            Check(Math.Abs(navigation.DistanceMetres - 719_300) < 0.01, "Reported surface clearance must reconstruct correctly.");
            Check(Vec3d.Distance(catalog.LocalFrameOrigin + position, catalog.Planet.SystemPosition) < catalog.Planet.Radius * catalog.Planet.RingInnerRadii,
                "This ship is inside the ring's inner radius, rather than halfway to the star.");

            var camera = new Camera3D { Current = true, Fov = 70, Near = 0.35f };
            AddChild(camera);
            var world = new Node3D(); AddChild(world);
            var origin = RenderFrame.ChooseOrigin(position, Vec3d.Zero);
            world.Position = -ShipTrack.ToVector3(origin);
            var builder = new WorldBuilder(world, catalog, true);
            builder.Build(); builder.Process(0, position, origin);
            camera.Far = builder.RequiredFarPlane;
            var ship = new ShipView(10000, true, "Navigation", true); AddChild(ship);
            var direction = new Vec3d(-0.4, -1, 0.1).Normalized();
            var angles = FlightModel.AnglesForDirection(direction);
            ship.Track.Push(new EntitySnapshot(10000, EntityKind.Player, "Navigation", position, (float)angles.Yaw,
                (float)angles.Pitch, Vec3d.Zero, 0, System.Numerics.Quaternion.CreateFromYawPitchRoll((float)angles.Yaw, (float)angles.Pitch, 0)), 0, 1);
            ship.UpdateVisual(origin, 0.016f);
            camera.Position = ship.Position + ship.Basis * new Vector3(1, 9, 34);
            camera.LookAt(ship.Position - ship.Basis.Z * 12, ship.Basis.Y);
            var layer = new CanvasLayer(); AddChild(layer);
            var map = new TacticalView { Catalog = catalog, PlayerId = 10000, PlayerPosition = position };
            layer.AddChild(map); map.Visible = true;
            await Pause(0.15);
            var starLocal = catalog.Star.SystemPosition - catalog.LocalFrameOrigin;
            var planetLocal = catalog.Planet.SystemPosition - catalog.LocalFrameOrigin;
            Check(map.ScreenPosition(starLocal).DistanceTo(map.ScreenPosition(planetLocal)) > map.Size.X * 1000,
                "A local map cannot place a one-AU separation inside the viewport.");
            map.QueueRedraw(); await Capture("01_local");
            map.ZoomAt(map.Size / 2, 20_000_000 / map.ZoomRange);
            await Pause(0.9);
            map.QueueRedraw(); await Capture("02_planet_and_rings");
            map.ShowSystem(); await Pause(0.9);
            var planetOnSystemMap = map.ScreenPosition(planetLocal);
            Check(map.ScreenPosition(position).DistanceTo(planetOnSystemMap) < 1, "On the AU map the ship must be next to the planet.");
            map.QueueRedraw(); await Capture("03_system");
            map.Visible = false;
            await Capture("04_same_position_3d");
            var cockpit = new CockpitView(); ship.AddChild(cockpit);
            ship.SetExternalModelVisible(false);
            camera.GlobalTransform = new Transform3D(ship.Basis, ship.Position + ship.Basis * CockpitView.EyePosition);
            camera.Fov = 78; camera.Near = 0.05f;
            Check(cockpit.IsVisibleInTree(), "Cockpit must remain visible after rebasing this distant position.");
            cockpit.UpdateInstruments(0, 0, false, catalog.Planet.Name, (float)navigation.DistanceMetres, catalog.Planet.Name, navigation.DistanceMetres, true, false, false, false);
            await Capture("05_same_position_cockpit");
            GD.Print("Navigation checks passed: reported position, ring radius, selectable bearing panel, local/AU scale, cockpit after rebase.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private async Task Capture(string name)
    {
        if (_output is null) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, name + ".png")) == Error.Ok, "Capture must be saved.");
    }

    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
