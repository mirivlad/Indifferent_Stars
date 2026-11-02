using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class StationDetailChecks : Node3D
{
    private readonly List<double> _frames = new();
    private ulong _lastFrame;
    private bool _measure;

    public override void _Process(double delta)
    {
        var now = Time.GetTicksUsec();
        if (_measure && _lastFrame != 0) _frames.Add((now - _lastFrame) / 1000.0);
        _lastFrame = now;
    }

    public override async void _Ready()
    {
        try
        {
            CollisionAssets.Validate();
            var baseline = OS.GetCmdlineUserArgs().Contains("--baseline");
            var output = System.Environment.GetEnvironmentVariable("IS_STATION_CAPTURE") ?? "/tmp/indifferent-station";
            Directory.CreateDirectory(output);
            if (OS.GetCmdlineUserArgs().Contains("--live"))
            {
                await CheckLiveDock(output);
                return;
            }
            var catalog = WorldCatalog.Generate(GameMain.DefaultSeed);
            var root = new Node3D(); AddChild(root);
            var builder = new WorldBuilder(root, catalog, true);
            builder.Build(); builder.Process(0, Vec3d.Zero, Vec3d.Zero, 0);
            var world = new GameWorld(catalog);
            world.AddPlayer("Station check");
            foreach (var entity in world.BuildSnapshot())
            {
                var ship = new ShipView(entity.Id, entity.Kind == EntityKind.Player, entity.Name, true);
                root.AddChild(ship);
                ship.Track.Push(entity, Time.GetTicksMsec() / 1000.0, 0);
                ship.UpdateVisual(Vec3d.Zero, 0);
            }
            var camera = new Camera3D { Current = true, Fov = 55, Near = .35f, Far = builder.RequiredFarPlane };
            AddChild(camera);
            if (!baseline)
            {
                var marker = builder.StationNode.FindChild("DockingPoint", true, false) as Node3D;
                Check(marker is not null, "Station must export its docking marker.");
                Check(marker!.GlobalPosition.DistanceTo(ShipTrack.ToVector3(catalog.Station.Position + ModelPose.StationDockingOffset)) < .001f,
                    "Rendered service berth must agree with the server docking point.");
                var textured = builder.StationNode.FindChildren("*", "MeshInstance3D", true, false)
                    .OfType<MeshInstance3D>().Count(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Any(i =>
                        mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D mat && mat.AlbedoTexture is not null));
                Check(textured >= 6, "Station materials must import their UV textures.");
                foreach (var mesh in builder.StationNode.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                for(var i=0;i<mesh.Mesh.GetSurfaceCount();i++)
                    if(mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D {AlbedoTexture: { } texture})
                        Check(texture.GetImage().HasMipmaps(),$"Mipmaps required for {mesh.Name} to avoid texture shimmer.");
            }
            if (OS.GetCmdlineUserArgs().Contains("--materials-only"))
            {
                GD.Print("STATION MATERIAL CHECKS PASSED: textures, mipmaps and docking marker.");
                GetTree().Quit();
                return;
            }
            var stationRotation = ModelPose.StationRotation(catalog.Station, 0);
            var solarTarget = ShipTrack.ToVector3(SurfaceMesh.Rotate(new Vec3d(308,0,0),stationRotation));
            var solarCamera = solarTarget + ShipTrack.ToVector3(SurfaceMesh.Rotate(new Vec3d(70,100,80),stationRotation));
            foreach (var (name, position, target) in new[]
            {
                ("00_hero", new Vector3(-620,480,760), new Vector3(0,70,0)),
                ("01_overview", new Vector3(620,480,760), new Vector3(0,70,0)),
                ("02_reverse", new Vector3(-620,-390,-730), new Vector3(0,40,0)),
                ("03_habitat", new Vector3(175,140,205), new Vector3(70,10,95)),
                ("04_solar", solarCamera, solarTarget),
                ("05_dock", new Vector3(95,340,130), new Vector3(0,229,0)),
                ("06_far", new Vector3(1400,1050,1650), new Vector3(0,60,0)),
            })
            {
                camera.Position = position; camera.LookAt(target, Vector3.Up);
                await ToSignal(GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png")) == Error.Ok, "Capture failed.");
            }
            if (OS.GetCmdlineUserArgs().Contains("--quick"))
            {
                GD.Print("STATION QUICK CAPTURES PASSED."); GetTree().Quit(); return;
            }
            camera.Position = new Vector3(620,480,760); camera.LookAt(new Vector3(0,70,0), Vector3.Up);
            foreach (var high in new[] {true,false})
            {
                builder.SetGlowEnabled(high); builder.SetVisualQuality(high, high ? 2 : 0);
                await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                _frames.Clear(); _measure = true;
                await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
                _measure = false;
                var frames = _frames.OrderBy(x=>x).ToArray();
                Check(frames.Length > 10, "Frame sample required.");
                GD.Print($"STATION TELEMETRY: high={high}, frames={frames.Length}, p95={frames[(int)(frames.Length*.95)]:F2}ms, " +
                    $"draw_calls={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}, " +
                    $"video_bytes={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed)}, " +
                    $"texture_bytes={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed)}, viewport={GetViewport().GetVisibleRect().Size}, " +
                    $"MSAA={GetViewport().Msaa3D}, scale={GetViewport().Scaling3DScale}, vsync={DisplayServer.WindowGetVsyncMode()}.");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,high ? "07_high.png" : "08_low.png"));
            }
            if (System.Environment.GetEnvironmentVariable("IS_STATION_MOVIE") == "1")
            {
                builder.SetGlowEnabled(true); builder.SetVisualQuality(true, 2);
                var movie = Path.Combine(output,"orbit"); Directory.CreateDirectory(movie);
                for (var i=0;i<120;i++)
                {
                    var angle = i*Mathf.Tau/120;
                    camera.Position = new Vector3(Mathf.Sin(angle)*950,380,Mathf.Cos(angle)*950);
                    camera.LookAt(new Vector3(0,70,0),Vector3.Up);
                    builder.Process(0, Vec3d.Zero, Vec3d.Zero, i*.1);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(movie,$"{i:D4}.png")) == Error.Ok,"Movie frame required.");
                }
            }
            GD.Print($"STATION DETAIL CHECKS PASSED: baseline={baseline}, imported station and gameplay context captured.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private async Task CheckLiveDock(string output)
    {
        System.Environment.SetEnvironmentVariable("IS_TRACE", "1");
        var game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>(); AddChild(game);
        ShipView? pilot = null;
        await Until(()=>
        {
            pilot = game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>()
                .FirstOrDefault(ship=>ship.IsPlayer && ship.Track.HasData);
            return pilot is not null;
        },12,"Live server required.");
        var catalog = WorldCatalog.Generate(GameMain.DefaultSeed);
        var map = game.FindChildren("*","Control",true,false).OfType<TacticalView>().First();
        Tap(Key.F1);
        for (var i=0;i<60 && map.TargetId != catalog.Station.Id;i++) { Tap(Key.Tab); await Pause(.04); }
        Check(map.TargetId == catalog.Station.Id,"Station must be selectable.");
        Tap(Key.F2); Tap(Key.H);
        await Until(()=>pilot!.Track.DockingGranted,3,"Dock request must be accepted.");
        Check(pilot!.Track.ApproachPoint is { } point && Vec3d.Distance(point,catalog.Station.Position+ModelPose.StationDockingOffset)<.001,
            "Client must receive the actual axial berth.");
        await Pause(3); await Capture("09_live_approach");
        Tap(Key.F3); await Pause(3); await Capture("10_live_cockpit");
        Tap(Key.F1); await Pause(1); await Capture("11_live_tactical");
        await Until(()=>pilot.Track.Docked,35,"Autopilot must dock at the visible berth.");
        Check(Vec3d.Distance(pilot.Track.WorldPosition,catalog.Station.Position+ModelPose.StationDockingOffset)<=15.1,"Docked hull at the berth required.");
        var station = game.FindChildren("*","CanvasLayer",true,false).OfType<StationView>().First();
        await Until(()=>station.Visible && station.HasMarket,4,"Station services must open.");
        await Capture("12_live_market");
        station.FindChildren("*","Button",true,false).OfType<Button>()
            .Single(button=>button.Text==Locale.Text("undock")).EmitSignal(Button.SignalName.Pressed);
        await Until(()=>!pilot.Track.Docked,3,"Undock must be authoritative.");
        Tap(Key.F2);
        Input.ParseInputEvent(new InputEventKey {Keycode=Key.R,PhysicalKeycode=Key.R,Pressed=true});
        await Pause(1.5);
        Input.ParseInputEvent(new InputEventKey {Keycode=Key.R,PhysicalKeycode=Key.R,Pressed=false});
        Check(pilot.Track.WorldPosition.Y>ModelPose.StationDockingOffset.Y+25,"Undocked ship must clear the berth by normal thrust.");
        await Capture("13_live_undock");
        GD.Print("STATION LIVE CHECKS PASSED: actual berth, approach in F2/F3/F1, dock, market and thrust after undock.");
        await game.QuitGracefully();

        async Task Capture(string name)
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"))==Error.Ok,"Capture required.");
        }
    }

    private async Task Until(Func<bool> condition,double seconds,string message)
    {
        for(var i=0;i<seconds/.05;i++) { if(condition()) return; await Pause(.05); }
        throw new InvalidOperationException(message);
    }
    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});
        Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
