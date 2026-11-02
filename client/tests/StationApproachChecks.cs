using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

public sealed partial class StationApproachChecks : Node
{
    private NetworkClient? _observer;
    private bool _measure;
    private ulong _lastFrame;
    private ulong _lastSnapshot;
    private ulong _lastTick;
    private readonly List<double> _frames = new();
    private readonly List<double> _snapshotGaps = new();
    private EntitySnapshot? _observerShip;
    private double _latestServerSeconds;
    private Quaternion _latestOrientation;
    private ShipView? _pilot;
    private readonly List<double> _renderAges = new();

    public override void _Process(double delta)
    {
        _observer?.Poll();
        _observer?.Update(delta,InputState.None);
        var now = Time.GetTicksUsec();
        if (_measure && _lastFrame != 0) _frames.Add((now-_lastFrame)/1000.0);
        if (_measure && _pilot is not null) _renderAges.Add((_latestServerSeconds-_pilot.Track.SampledServerSeconds)*1000);
        _lastFrame = now;
    }

    public override async void _Ready()
    {
        try
        {
            System.Environment.SetEnvironmentVariable("IS_TRACE","0");
            var output = System.Environment.GetEnvironmentVariable("IS_APPROACH_CAPTURE") ?? "/tmp/indifferent-station-approach";
            Directory.CreateDirectory(output);
            var game = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>(); AddChild(game);
            var menu = game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>()
                .Single(button=>button.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            ShipView? pilot = null;
            await Until(()=>
            {
                pilot = game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>()
                    .FirstOrDefault(ship=>ship.IsPlayer && ship.Track.HasData);
                return pilot is not null;
            },12,"Real client connection required.");
            // Obtain the actual game's transport; send only ordinary authoritative intents.
            var transport = typeof(GameMain).GetField("_net",BindingFlags.Instance|BindingFlags.NonPublic)!
                .GetValue(game) as NetworkClient ?? throw new InvalidOperationException("Game transport unavailable.");
            transport.SnapshotReceived += snapshot=>
            {
                if(snapshot.Entities.FirstOrDefault(entity=>entity.Id==pilot!.Id) is { } own)
                    _latestOrientation = new Quaternion(own.Orientation.X,own.Orientation.Y,own.Orientation.Z,own.Orientation.W);
                _latestServerSeconds = snapshot.ServerTimeMs/1000.0;
                if(snapshot.Tick<=_lastTick) return;
                var now = Time.GetTicksUsec();
                if(_measure && _lastSnapshot!=0) _snapshotGaps.Add((now-_lastSnapshot)/1000.0);
                _lastSnapshot=now; _lastTick=snapshot.Tick;
            };
            _pilot=pilot;
            var args = OS.GetCmdlineUserArgs();
            var serverIndex = Array.IndexOf(args,"--server");
            Check(serverIndex>=0 && serverIndex+1<args.Length,"Explicit owned test server required.");
            var endpoint = args[serverIndex+1].Split(':');
            _observer = new NetworkClient(endpoint[0],int.Parse(endpoint[1]),"Approach observer");
            _observer.SnapshotReceived += snapshot=>
            {
                _observerShip = snapshot.Entities.FirstOrDefault(entity=>entity.Id==_observer.PlayerId) ?? _observerShip;
            };
            _observer.Start();
            await Until(()=>_observer.State==ConnectionState.Connected,8,"Second independent client required.");
            GetViewport().Scaling3DScale=1;
            GetViewport().Msaa3D=Viewport.Msaa.Msaa4X;
            game.GetNode<Node3D>("StaticWorld").GetNode<DirectionalLight3D>("Starlight").ShadowEnabled=true;
            var environment = game.FindChildren("*","WorldEnvironment",true,false).OfType<WorldEnvironment>().Single();
            environment.Environment.GlowEnabled=true;
            var catalog = WorldCatalog.Generate(GameMain.DefaultSeed);
            var pose = ModelPose.StationRotation(catalog.Station,0);
            var point = SurfaceMesh.Rotate(new Vec3d(2.9,.36,0)*ModelPose.StationScale,pose);
            var opposite = SurfaceMesh.Rotate(new Vec3d(-2.9,.36,0)*ModelPose.StationScale,pose);
            Tap(Key.F2);
            _measure=true;
            transport.RequestApproach(0,waypoint:point);
            _observer.RequestApproach(0,waypoint:opposite);
            await Until(()=>pilot!.Track.ApproachTargetId==0 && Vec3d.Distance(pilot.Track.WorldPosition,point)<35 &&
                _observerShip is {ApproachTargetId:0} other && Vec3d.Distance(other.Position,opposite)<35,
                45,"Both pilots must approach the station by normal engines.");
            var results = new List<object> { Summary("approach",Array.Empty<double>()) };
            var camera = game.GetNode<Camera3D>("ViewCamera");
            var allResponses = new List<double>();
            foreach(var (key,name) in new[] {(Key.F2,"external"),(Key.F3,"cockpit")})
            {
                Tap(key); await Pause(1);
                _frames.Clear(); _snapshotGaps.Clear(); _renderAges.Clear();
                var response = new List<double>();
                var rawResponse = new List<double>();
                for(var pulse=0;pulse<6;pulse++)
                {
                    var before = camera.GlobalBasis.GetRotationQuaternion();
                    var start = Time.GetTicksUsec();
                    var rawBefore = _latestOrientation;
                    double? rawLatency = null;
                    var turnKey = pulse%2==0 ? Key.Left : Key.Right;
                    SetKey(turnKey,true);
                    try
                    {
                        await Until(()=>
                        {
                            if(rawLatency is null && Mathf.Abs(rawBefore.Dot(_latestOrientation))<.99999f)
                                rawLatency=(Time.GetTicksUsec()-start)/1000.0;
                            return Mathf.Abs(before.Dot(camera.GlobalBasis.GetRotationQuaternion()))<.99999f;
                        },
                            1,"Keyboard input must reach the visible camera.");
                        response.Add((Time.GetTicksUsec()-start)/1000.0);
                        rawResponse.Add(rawLatency ?? -1);
                        await Pause(.15);
                    }
                    finally {SetKey(turnKey,false);}
                    await Pause(.7);
                }
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,name+".png"));
                var entry = Summary(name,response.ToArray()); results.Add(entry);
                allResponses.AddRange(response);
                GD.Print(JsonSerializer.Serialize(entry));
                GD.Print($"INPUT STAGES {name}: raw={string.Join(',',rawResponse.Select(value=>value.ToString("F1")))}, visible={string.Join(',',response.Select(value=>value.ToString("F1")))}.");
            }
            _measure=false;
            File.WriteAllText(Path.Combine(output,"metrics.json"),JsonSerializer.Serialize(new
            {
                seed=GameMain.DefaultSeed, geometry=SurfaceMesh.Fingerprint, station_scale=ModelPose.StationScale,
                viewport=GetViewport().GetVisibleRect().Size.ToString(), scale=GetViewport().Scaling3DScale,
                msaa=GetViewport().Msaa3D.ToString(), glow=environment.Environment.GlowEnabled, shadows=true,
                observer_count=2, pilot_position=pilot!.Track.WorldPosition.ToString(),
                metrics=results,
            },new JsonSerializerOptions {WriteIndented=true}));
            Check(Percentile(allResponses,.95)<250,"Visible keyboard response p95 must remain below 250ms at 20Hz.");
            _observer.Dispose(); _observer=null;
            GD.Print("STATION APPROACH CHECKS PASSED: two clients, full-quality external/cockpit and real keyboard-to-camera response.");
            await game.QuitGracefully();
        }
        catch(Exception error)
        {
            _observer?.Dispose();
            GD.PrintErr(error); GetTree().Quit(1);
        }
    }

    private object Summary(string scenario,double[] responses) => new
    {
        scenario,frames=_frames.Count,frame_p95_ms=Percentile(_frames,.95),frame_p99_ms=Percentile(_frames,.99),
        frame_max_ms=_frames.DefaultIfEmpty().Max(),snapshot_gap_p95_ms=Percentile(_snapshotGaps,.95),
        input_samples=responses.Length,input_p95_ms=Percentile(responses,.95),input_max_ms=responses.DefaultIfEmpty().Max(),
        render_age_p95_ms=Percentile(_renderAges,.95),
    };
    private static double Percentile(IEnumerable<double> source,double percentile)
    {
        var values=source.OrderBy(value=>value).ToArray();
        return values.Length==0 ? 0 : values[Math.Min(values.Length-1,(int)(values.Length*percentile))];
    }
    private async Task Until(Func<bool> condition,double seconds,string message)
    {
        var start=Time.GetTicksUsec();
        while((Time.GetTicksUsec()-start)/1_000_000.0<seconds) {if(condition()) return;await Pause(.01);}
        throw new InvalidOperationException(message);
    }
    private async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static void SetKey(Key key,bool pressed) => Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private static void Tap(Key key) {SetKey(key,true);SetKey(key,false);}
    private static void Check(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
}
