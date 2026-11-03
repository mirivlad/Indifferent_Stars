using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Client;

public enum ViewMode
{
    External,
    Tactical,
    Cockpit,
}

/// <summary>
/// Client entry point. All gameplay state arrives from the server snapshots; this
/// node only builds visuals, reads input and renders the three representations.
/// </summary>
public sealed partial class GameMain : Node3D
{
    public const int DefaultSeed = 20261101;

    private NetworkClient? _net;
    private WorldCatalog _catalog = null!;
    private WorldBuilder? _worldBuilder;
    private Node3D _staticRoot = null!;
    private Node3D _dynamicRoot = null!;
    private Node3D _remoteRoot = null!;
    private ShipView? _playerShip;
    private CockpitView? _cockpit;
    private Camera3D _camera = null!;
    private SpotLight3D? _cameraFill;
    private Hud _hud = null!;
    private RadioChat _radio = null!;
    private Input.MouseModeEnum _beforeChat;
    private TacticalView _tactical = null!;
    private CanvasLayer _tacticalLayer = null!;
    private PopupMenu _contactMenu = null!;
    private Vec3d _mapDestination;
    private StationView _stationView = null!;
    private ClientSettings _settings = null!;
    private SettingsMenu _settingsMenu = null!;
    private WarpEffect _warpEffect = null!;
    private WarpStage _previousWarp;
    private ShipAudio _audio = null!;

    private readonly Dictionary<uint, ShipView> _remoteShips = new();
    private readonly Dictionary<uint, double> _remoteSeen = new();
    private ViewMode _mode = ViewMode.External;
    private bool _highQuality = true;
    private bool _cameraSnap = true;
    private Quaternion _chaseRotation = Quaternion.Identity;
    private float _cameraDistance = new Vector3(1, 9, 34).Length();
    private float _targetCameraDistance = new Vector3(1, 9, 34).Length();
    public const float MinimumCameraDistance = 24;
    public const float MaximumCameraDistance = 650;
    private Vector2 _cameraOrbit;
    private Vector2 _cockpitLook;
    private Input.MouseModeEnum _beforeLook;
    private bool _lookCaptured;
    private bool LookingAround => _mode != ViewMode.Tactical &&
        _radio?.IsTyping != true && _navigation?.IsTyping != true && _navigation?.IsModal != true && (_settings.Pressed("camera_look") || Input.IsMouseButtonPressed(MouseButton.Middle));
    private bool _quitting;
    private uint _playerId;
    private uint _targetId;
    private bool _hasTarget;
    private float _scanCooldown;
    private float _impactShake;
    private float _impactPhase;
    private float _marketRetry;
    private readonly bool _trace = System.Environment.GetEnvironmentVariable("IS_TRACE") == "1";
    private Vec3d _traceLast;
    private bool _traceReady;
    private ulong _traceAt;
    private float _lastHudUpdate;
    private ulong _serverTick;
    private string _sectorId = "";
    private InputState _input = InputState.None;
    private bool _flightAssist;
    private bool _cruise;
    private Vec3d _renderOrigin = Vec3d.Zero;
    private Vector2 _mouseTurn;
    private ulong _mouseTurnAt;

    private string _playerName = "Captain";
    private string _serverHost = "127.0.0.1";
    private int _serverPort = 7777;
    private float _scanRange = 4200f;

    public override void _Ready()
    {
        // All telemetry is formatted with one culture so the HUD reads the same
        // on every machine ("2.4 km", never "2,4 km").
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
            System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture =
            System.Globalization.CultureInfo.InvariantCulture;

        try { CollisionAssets.Validate(); }
        catch (InvalidOperationException error) { GD.PushError(error.Message); GetTree().Quit(1); return; }
        _settings = ClientSettings.Load();
        GetTree().AutoAcceptQuit = false;
        _flightAssist = _settings.DefaultFlightAssist;
        Locale.Load(_settings.Language);
        ParseCommandLine();
        _audio = new ShipAudio { Name = "ShipAudio" };
        AddChild(_audio);
        _catalog = WorldCatalog.Generate(DefaultSeed);

        _staticRoot = new Node3D { Name = "StaticWorld" };
        AddChild(_staticRoot);
        _dynamicRoot = new Node3D { Name = "DynamicWorld" };
        AddChild(_dynamicRoot);
        _remoteRoot = new Node3D { Name = "RemoteShips" };
        _dynamicRoot.AddChild(_remoteRoot);

        SetupCamera();
        SetupUi();
        _warpEffect = new WarpEffect();
        AddChild(_warpEffect);
        RebuildStaticWorld(DefaultSeed);

        _hud.SetSystem(_catalog.SystemName);
        _hud.SetMode("external", Locale.Text("link_connecting_short"));

        _settingsMenu = new SettingsMenu(_settings, ApplySettings, StartConnection, () => { _ = QuitGracefully(); });
        AddChild(_settingsMenu);
        ApplySettings();
        if (_captureDir is not null || _trace)
        {
            _settingsMenu.Visible = false;
            StartConnection();
        }

        GetWindow().Title = $"Indifferent Stars — Second Flight — {_playerName}";
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
    }

    private void StartConnection()
    {
        if (_net is not null) return;
        var split = _settings.Server.Split(':', 2);
        _serverHost = split[0];
        _serverPort = split.Length > 1 && int.TryParse(split[1], out var port) && port is > 0 and <= 65535 ? port : 7777;
        _playerName = string.IsNullOrWhiteSpace(_settings.Callsign) ? "Captain" : _settings.Callsign.Trim();
        _hud.ServerAddress = _settings.Server;
        _net = new NetworkClient(_serverHost, _serverPort, _playerName);
        _net.WelcomeReceived += OnWelcome;
        _net.JoinRejected += OnJoinRejected;
        _net.SnapshotReceived += OnSnapshot;
        _net.CommsCompleted += reply =>
        {
            _audio.RadioCue();
            var sender=_catalog.Find((int)reply.TargetId) is {Kind:BodyKind.Station} respondingStation?respondingStation.Name:
                _remoteShips.TryGetValue(reply.TargetId,out var ship)?ship.DisplayName:Locale.Text("radio_heading");
            var text=_settings.Language == "ru" ? reply.RussianMessage : reply.Message;
            if(text.StartsWith(sender+": ",StringComparison.Ordinal))text=text[(sender.Length+2)..];
            _radio.Radio(sender,text,reply.TargetId);
        };
        _net.ChatReceived += _radio.Receive;
        _net.ChatRejected += result => _radio.Notice(RadioChat.Notifications,Locale.Text("chat_error_"+(int)result.Error));
        _net.ChatTimedOut += channel => _radio.Notice(RadioChat.Notifications,Locale.Text("chat_timeout"));
        _net.ScanCompleted += OnScanCompleted;
        _net.ScanTimedOut += () => _hud.ShowScanResult(Locale.Text("scan_rejected"), Locale.Text("scan_timeout"), false);
        _net.WarpCompleted += OnWarpCompleted;
        _net.ApproachCompleted += OnApproachCompleted;
        ConnectNavigation();
        _net.DockCompleted += OnDockCompleted;
        _net.MarketReceived += OnMarketReceived;
        _net.TradeCompleted += OnTradeCompleted;
        _net.TransportError += OnTransportError;
        _net.Start();

        GD.Print($"Indifferent Stars client: {_playerName} -> {_serverHost}:{_serverPort}");
    }

    public async System.Threading.Tasks.Task QuitGracefully(int exitCode = 0)
    {
        if (_quitting) return;
        _quitting = true;
        _audio.Stop();
        _playerShip?.StopImpactAudio();
        foreach(var ship in _remoteShips.Values)ship.StopImpactAudio();
        // Let the audio server consume pending playback stops before tearing down resources.
        await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit(exitCode);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) _ = QuitGracefully();
    }

    private void ApplySettings()
    {
        GetViewport().Scaling3DScale = _settings.RenderScale;
        GetViewport().Msaa3D = _settings.Antialiasing ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        var windowMode = _settings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != windowMode) DisplayServer.WindowSetMode(windowMode);
        var dimensions = _settings.Resolution.Split('x');
        if (!_settings.Fullscreen && dimensions.Length == 2 && int.TryParse(dimensions[0], out var width) && int.TryParse(dimensions[1], out var height) && width >= 640 && height >= 480)
        {
            var size = new Vector2I(width, height);
            if (DisplayServer.WindowGetSize() != size) DisplayServer.WindowSetSize(size);
        }
        SetVolume("Master", _settings.MasterVolume);
        SetVolume("Music", _settings.MusicVolume);
        SetVolume("Engines", _settings.EngineVolume);
        SetVolume("Effects", _settings.EffectsVolume);
        SetVolume("UI", _settings.UiVolume);
        SetVolume("Radio", _settings.RadioVolume);
        _highQuality = _settings.Glow;
        _worldBuilder?.SetGlowEnabled(_settings.Glow && _mode != ViewMode.Tactical);
        _worldBuilder?.SetVisualQuality(_settings.Shadows, _settings.SkyDetail);
        _playerShip?.SetEngineEffects(_settings.EngineEffects);
        _playerShip?.SetParticles(_settings.Particles);
        foreach (var ship in _remoteShips.Values)
        {
            ship.SetEngineEffects(_settings.EngineEffects);
            ship.SetParticles(_settings.Particles);
        }
        _audio.SetMusicDirectory(_settings.MusicDirectory);
        _hud.RefreshLanguage();
        _navigation.RefreshLanguage();
        _tactical.RefreshLanguage();
        _radio.RefreshLanguage();
        _stationView?.RefreshLanguage();
        BuildContactMenu();
        if (_playerShip is not null)
            SetMode(_mode);
    }

    private static void SetVolume(string name, float value)
    {
        var bus = AudioServer.GetBusIndex(name);
        if (bus >= 0)
        {
            AudioServer.SetBusMute(bus, value <= 0);
            AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(0.0001f, value)));
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _net?.Poll();
        var frameTime = Time.GetTicksUsec() / 1_000_000.0;
        UpdateRenderOrigin(frameTime);

        var looking = LookingAround && !_settingsMenu.IsOpen;
        if (looking && !_lookCaptured)
        {
            _beforeLook = Input.MouseMode;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _lookCaptured = true;
        }
        else if (!looking && _lookCaptured)
        {
            if (!_settingsMenu.IsOpen && !_radio.IsTyping && _mode != ViewMode.Tactical) Input.MouseMode = _beforeLook;
            _lookCaptured = false;
        }

        _input = ReadInput();
        _net?.Update(delta, _input);
        if (_playerShip is not null)
        {
            _playerShip.Boosting = _input.Boost || _playerShip.Track.Boosting;
            _playerShip.UpdateVisual(_renderOrigin, dt, frameTime);
            var maneuver = Math.Max(Math.Abs(_input.Strafe), Math.Max(Math.Abs(_input.Vertical),
                Math.Max(Math.Abs(_input.Roll), Math.Max(Math.Abs(_input.Steer), Math.Abs(_input.Pitch)))));
            _audio.SetFlight(_playerShip.Track.Throttle, maneuver,
                _playerShip.Boosting, _input.Brake || _playerShip.EngineBraking,
                _playerShip.Track.Cruising, _net?.State == ConnectionState.Connected && !_playerShip.Track.Docked);
            if (_trace && _playerShip.Track.HasData)
            {
                var p = _playerShip.Track.WorldPosition;
                var at = Time.GetTicksUsec();
                if (_traceReady && dt > 0)
                {
                    var moved = Vec3d.Distance(p, _traceLast);
                    var wall = (at - _traceAt) / 1_000_000.0;
                    GD.Print($"TRACE dt={dt:F4} wall={wall:F4} moved={moved:F3} v={_playerShip.Track.Velocity.Length():F1} fps={Engine.GetFramesPerSecond()}");
                }
                _traceLast = p;
                _traceAt = at;
                _traceReady = true;
            }
            var docked = _playerShip.Track.Docked;
            if (_stationView.Visible != docked)
            {
                _stationView.SetDocked(docked, _catalog.Find((int)_playerShip.Track.DockingStationId)?.Name??_catalog.Station.Name);
                _marketRetry = 0;
                if (docked)
                    Input.MouseMode = Input.MouseModeEnum.Visible;
            }
            if (docked && !_stationView.HasMarket && _net is not null)
            {
                _marketRetry -= dt;
                if (_marketRetry <= 0)
                {
                    _net.RequestMarket();
                    _marketRetry = 0.8f;
                }
            }
        }

        UpdateRemoteShips(dt, frameTime);
        UpdateTargeting();
        UpdateSector();
        UpdateTactical();
        var warpStage = _playerShip?.Track.WarpStage ?? WarpStage.None;
        var warpProgress = _playerShip?.Track.WarpProgress ?? 0;
        if (_previousWarp != warpStage)
        {
            if (warpStage == WarpStage.Recovery)
                _hud.ShowToast(Locale.Text("warp_recovery"), duration: 8);
            else if (warpStage == WarpStage.None && _previousWarp != WarpStage.None)
                _hud.ShowToast(Locale.Text(_previousWarp == WarpStage.Charging ? "warp_cancelled" : "warp_arrived"), duration: 4);
            _previousWarp = warpStage;
        }
        _warpEffect.SetState(warpStage, warpProgress, _mode == ViewMode.Tactical, _settings.Particles);
        _audio.SetWarp(warpStage, warpProgress);
        _hud.SetWarpState(warpStage, warpProgress);
        UpdateCamera(dt);
        _impactShake*=Mathf.Exp(-dt*6);_impactPhase+=dt;
        if(_mode!=ViewMode.Tactical && _impactShake>.001f)
        {
            var wobble=new Vector3(Mathf.Sin(_impactPhase*71),Mathf.Sin(_impactPhase*93),Mathf.Sin(_impactPhase*47));
            _camera.Basis*=new Basis(Quaternion.FromEuler(wobble*_impactShake*.012f));
            _camera.Position+=_camera.Basis*wobble*_impactShake*.025f;
        }
        _worldBuilder?.Process(delta, _playerShip?.Track.WorldPosition ?? Vec3d.Zero, _renderOrigin,
            _playerShip?.Track.HasData == true ? _playerShip.Track.SampledServerSeconds : null);
        UpdateCameraClipping();

        _scanCooldown = Mathf.Max(0f, _scanCooldown - dt);
        if (_net is not null)
        {
            _hud.SetConnection(_net.State, _net.StatusText, _net.PingMs);
            if (_net.Welcome is not null && _net.State == ConnectionState.Connected)
                _serverTick = _net.LastSnapshotTick;
        }

        UpdateNavigation();
        UpdateHud(dt);
        _hud.SetWarpState(warpStage, warpProgress);
        UpdateCapture(dt);
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key ||
            key.Keycode != _settings.Keys["cursor"] || _settingsMenu.IsOpen || _radio.IsTyping || _navigation.IsModal) return;
        var typing = _navigation.IsTyping;
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        Input.MouseMode = typing || _mode == ViewMode.Tactical || Input.MouseMode == Input.MouseModeEnum.Captured
            ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        _mouseTurn = Vector2.Zero; _mouseTurnAt = 0;
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_settingsMenu.IsOpen || _radio.IsTyping || _navigation.IsTyping || _navigation.IsModal) return;
        if(@event is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Enter})
        {
            _radio.BeginTyping();GetViewport().SetInputAsHandled();return;
        }
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            var action = _settings.Keys.FirstOrDefault(pair => pair.Value == key.Keycode).Key;
            switch (action)
            {
                case "view_2d":
                    SetMode(ViewMode.Tactical);
                    break;
                case "view_3d":
                    SetMode(ViewMode.External);
                    break;
                case "view_cockpit":
                    SetMode(ViewMode.Cockpit);
                    break;
                case "quality":
                    ToggleQuality();
                    break;
                case "scan":
                    RequestScan();
                    break;
                case "warp":
                    RequestWarp();
                    break;
                case "approach":
                    ToggleAutopilot();
                    break;
                case "dock":
                    RequestDock();
                    break;
                case "cruise":
                    _cruise = !_cruise;
                    _hud.ShowToast(Locale.Text(_cruise ? "cruise_request_hint" : "cruise_stop_hint"), duration: 6);
                    break;
                case "assist" when _mode != ViewMode.Tactical:
                    _flightAssist = !_flightAssist;
                    break;
                case "map_ship" when _mode == ViewMode.Tactical:
                    _tactical.CenterOnPlayer();
                    break;
                case "map_system" when _mode == ViewMode.Tactical:
                    _tactical.ShowSystem();
                    break;
            }
            if (action == "menu")
            {
                if (Input.MouseMode == Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Visible;
                else _settingsMenu.ShowMenu(true);
            }
        }
        else if (@event is InputEventMouseButton zoom && zoom.Pressed && _mode == ViewMode.External &&
            zoom.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _targetCameraDistance = Mathf.Clamp(_targetCameraDistance *
                (zoom.ButtonIndex == MouseButton.WheelUp ? 1 / 1.15f : 1.15f), MinimumCameraDistance, MaximumCameraDistance);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion look && LookingAround)
        {
            var angles = _mode == ViewMode.Cockpit ? _cockpitLook : _cameraOrbit;
            angles.X = Mathf.Wrap(angles.X - look.Relative.X * _settings.CameraSensitivity, -Mathf.Pi, Mathf.Pi);
            angles.Y = Mathf.Clamp(angles.Y - look.Relative.Y * _settings.CameraSensitivity, -1.35f, 1.35f);
            if (_mode == ViewMode.Cockpit) _cockpitLook = angles;
            else _cameraOrbit = angles;
            _mouseTurn = Vector2.Zero;
            _mouseTurnAt = 0;
        }
        else if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured && _mode != ViewMode.Tactical)
        {
            _mouseTurn = motion.Relative;
            _mouseTurnAt = Time.GetTicksMsec();
        }
        else if (@event is InputEventMouseMotion pan && _mode == ViewMode.Tactical && (pan.ButtonMask & MouseButtonMask.Middle) != 0)
        {
            _tactical.Pan(pan.Relative);
        }
        else if (@event is InputEventMouseButton capture && capture.Pressed && capture.ButtonIndex == MouseButton.Left && _mode != ViewMode.Tactical)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventMouseButton mouse && mouse.Pressed && _mode == ViewMode.Tactical)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
                _tactical.ZoomAt(mouse.Position, 0.65);
            else if (mouse.ButtonIndex == MouseButton.WheelDown)
                _tactical.ZoomAt(mouse.Position, 1 / 0.65);
            else if (mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                var contact = BuildContacts().Where(c => c.Id != _playerId && _tactical.ZoomRange < 100_000_000)
                    .Select(c => (Contact: c, Distance: _tactical.ScreenPosition(new Vec3d(c.Position.X, c.Position.Y, c.Position.Z)).DistanceTo(mouse.Position)))
                    .Where(c => c.Distance < 20).OrderBy(c => c.Distance).FirstOrDefault();
                var selectedId = contact.Contact.Id;
                var selectedDistance = selectedId == 0 ? float.MaxValue : contact.Distance;
                if (_tactical.ZoomRange >= 100_000_000)
                {
                    foreach (var celestial in _catalog.Celestials)
                    {
                        var distance = _tactical.ScreenPosition(celestial.SystemPosition - _catalog.LocalFrameOrigin).DistanceTo(mouse.Position);
                        if (distance < 20 && distance < selectedDistance)
                        {
                            selectedId = (uint)celestial.Id;
                            selectedDistance = distance;
                        }
                    }
                }
                if (selectedId != 0)
                {
                    _targetId = selectedId;
                    _hasTarget = true;
                    if (mouse.ButtonIndex == MouseButton.Right)
                    {
                        BuildContactMenu();
                        _contactMenu.Position = (Vector2I)GetViewport().GetMousePosition();
                        _contactMenu.Popup();
                    }
                }
                else if (mouse.ButtonIndex == MouseButton.Right)
                {
                    _mapDestination = _tactical.WorldPositionAt(mouse.Position);
                    ShowWaypointMenu();
                }
            }
        }
    }

    public override void _ExitTree()
    {
        _net?.Dispose();
        _net = null;
    }

    private void SetupCamera()
    {
        _camera = new Camera3D
        {
            Name = "ViewCamera",
            Current = true,
            Fov = 70,
            Near = 0.35f,
            Far = 900_000,
        };
        AddChild(_camera);

        // Soft fill from the camera reveals the chase-view hull without flattening space.
        var cameraFill = new SpotLight3D
        {
            Name = "CameraFill",
            LightColor = new Color(0.62f, 0.76f, 1.0f),
            LightEnergy = 55.0f,
            SpotAngle = 52,
            SpotRange = 90f,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 55,
            DistanceFadeShadow = 80,
            ShadowEnabled = false,
        };
        _camera.AddChild(cameraFill);
        _cameraFill = cameraFill;

        // A short-range omni near the eye lifts the hull out of pure silhouette.
        var hullFill = new OmniLight3D
        {
            Name = "HullFill",
            Position = new Vector3(0, 0, -1.5f),
            LightColor = new Color(0.58f, 0.72f, 0.95f),
            LightEnergy = 2.4f,
            OmniRange = 26f,
            ShadowEnabled = false,
        };
        _camera.AddChild(hullFill);

        var station = _catalog.Station.Position;
        _camera.GlobalPosition = new Vector3((float)station.X + 450, 520, (float)station.Z + 1650);
        _camera.LookAt(new Vector3((float)station.X, (float)station.Y, (float)station.Z), Vector3.Up);
    }

    private void SetupUi()
    {
        _hud = new Hud { Name = "Hud", ServerAddress = _settings.Server };
        AddChild(_hud);
        _radio=new RadioChat();AddChild(_radio);
        _hud.NoticeRaised += text => _radio.Notice(RadioChat.Notifications,text);
        SetupNavigation();
        _radio.SignalSelected += position => { _mapDestination=position; ShowWaypointMenu(); };
        _radio.SendRequested+=(channel,text)=>{if(_net?.SendChat(channel,text)!=true)_radio.Notice(RadioChat.Notifications,Locale.Text("no_connection"));};
        _radio.TypingChanged+=typing=>
        {
            if(typing){_beforeChat=_lookCaptured?_beforeLook:Input.MouseMode;Input.MouseMode=Input.MouseModeEnum.Visible;}
            else if(_settingsMenu?.IsOpen != true)Input.MouseMode=_beforeChat;
            _mouseTurn=Vector2.Zero;_mouseTurnAt=0;
        };

        _tacticalLayer = new CanvasLayer { Name = "TacticalLayer", Layer = 5 };
        AddChild(_tacticalLayer);
        _tactical = new TacticalView { Name = "TacticalView", Catalog = _catalog };
        _tacticalLayer.AddChild(_tactical);
        _contactMenu = new PopupMenu();
        _contactMenu.IdPressed += id =>
        {
            if (id == 20) ShowObjectInformation();
            else if (id == 21) { _hasTarget=true; }
            else if (id == 22) AddRoutePoint(new NavigationPoint(_targetId));
            else if (id == 23) AddRoutePoint(new NavigationPoint(0,_mapDestination));
            else if (id == 24) OpenNavigationDistance(true);
            else if (id == 25) OpenNavigationDistance(false);
            else if (id == 26) OpenWaypointWarpDistance();
            else if (id == 10) _net?.RequestComms(_targetId);
            else if (id == 11)
            {
                if (_playerShip?.Track is {Docked:true} dockedTrack && dockedTrack.DockingStationId==_targetId) _stationView.Visible = true;
                else _hud.ShowToast(Locale.Text("trade_dock_first"), duration: 5);
            }
            else if (id == 1) RequestApproach();
            else if (id == 2) RequestScan();
            else if (id == 3) RequestWarp();
            else if (id == 4) RequestDock();
            else if (id == 7) RequestApproach(true);
            else if (id == 14) _net?.RequestWarp(0, _mapDestination);

        };
        AddChild(_contactMenu);
        BuildContactMenu();

        _stationView = new StationView();
        AddChild(_stationView);
        _stationView.CommsRequested += () => _net?.RequestComms(_playerShip?.Track.DockingStationId??(uint)_catalog.Station.Id);
        _stationView.UndockRequested += RequestUndock;
        _stationView.TradeRequested += (commodity, quantity, buy) => _net?.RequestTrade(commodity, quantity, buy);
    }

    private void BuildContactMenu()
    {
        _contactMenu.Clear();
        _contactMenu.AddItem(Locale.Text("object_information"),20);
        _contactMenu.AddItem(Locale.Text("target_lock"),21);
        _contactMenu.AddItem(Locale.Text("route_add"),22);
        _contactMenu.AddSeparator();
        _contactMenu.AddItem(Locale.Text("context_approach"), 1);
        _contactMenu.AddItem(Locale.Text("context_scan"), 2);
        _contactMenu.AddItem(Locale.Text("context_warp"), 3);
        _contactMenu.AddItem(Locale.Text("context_warp_distance"), 25);
        if (_catalog.FindCelestial((int)_targetId) is not null)
            _contactMenu.AddItem(Locale.Text("context_orbit"),24);
        if (_catalog.Find((int)_targetId)?.Kind==BodyKind.Station) _contactMenu.AddItem(Locale.Text("context_dock"), 4);
        if (_remoteShips.ContainsKey(_targetId)) _contactMenu.AddItem(Locale.Text("context_follow"), 7);
        _contactMenu.AddSeparator();
        _contactMenu.AddItem(Locale.Text("context_comms"), 10);
        _contactMenu.AddItem(Locale.Text("context_trade"), 11);
        _contactMenu.SetItemDisabled(_contactMenu.GetItemIndex(11), _catalog.Find((int)_targetId)?.Kind!=BodyKind.Station);
        _contactMenu.AddItem(Locale.Text("context_attack_unavailable"), 12);
        _contactMenu.SetItemDisabled(_contactMenu.GetItemIndex(12), true);
        _contactMenu.AddItem(Locale.Text("context_mine_unavailable"), 13);
        _contactMenu.SetItemDisabled(_contactMenu.GetItemIndex(13), true);
    }

    private void RequestUndock() => _net?.RequestDock(_playerShip?.Track.DockingStationId??(uint)_catalog.Station.Id, true);

    private void RebuildStaticWorld(int seed, int systemIndex = 0)
    {
        _catalog = WorldCatalog.Generate(seed,systemIndex);
        if (_worldBuilder is not null)
        {
            _staticRoot.QueueFree();
            _staticRoot = new Node3D { Name = "StaticWorld" };
            AddChild(_staticRoot);
            MoveChild(_staticRoot, 0);
        }

        _worldBuilder = new WorldBuilder(_staticRoot, _catalog, _highQuality);
        _worldBuilder.Build();
        _worldBuilder.SetVisualQuality(_settings.Shadows, _settings.SkyDetail);
        _tactical.Catalog = _catalog;
        GD.Print($"World built: {_catalog.SystemName}, seed {seed}, {_catalog.Asteroids.Count} asteroids.");
    }

    private void SetMode(ViewMode mode)
    {
        _mode = mode;
        if (mode == ViewMode.Tactical)
            Input.MouseMode = Input.MouseModeEnum.Visible;
        else if (_settings.AutoCapture && !_settingsMenu.IsOpen)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        _tactical.Visible = mode == ViewMode.Tactical;
        _tacticalLayer.Visible = mode == ViewMode.Tactical;
        _staticRoot.Visible = mode != ViewMode.Tactical;
        _dynamicRoot.Visible = mode != ViewMode.Tactical;

        if (_cockpit is not null)
            _cockpit.Visible = mode == ViewMode.Cockpit;
        _playerShip?.SetExternalModelVisible(mode != ViewMode.Cockpit);

        UpdateCameraClipping();
        if (_cameraFill is not null)
            _cameraFill.Visible = mode == ViewMode.External;
        _cameraSnap = true;

        var (title, subtitle) = mode switch
        {
            ViewMode.Tactical => ("tactical", Locale.Text("mode_tactical_subtitle")),
            ViewMode.Cockpit => ("cockpit", Locale.Text("mode_cockpit_subtitle")),
            _ => ("external", Locale.Text("mode_external_subtitle")),
        };
        _hud.SetMode(title, subtitle);
        if (_playerShip is not null)
            _worldBuilder?.SetGlowEnabled(_highQuality && mode != ViewMode.Tactical);
    }

    private void ToggleQuality()
    {
        _highQuality = !_highQuality;
        _settings.Glow = _highQuality;
        _settings.Save();
        _worldBuilder?.SetGlowEnabled(_highQuality && _mode != ViewMode.Tactical);
        _hud.SetQuality(_highQuality);
    }

    private void UpdateRemoteShips(float dt, double frameTime)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var ship in _remoteShips.Values)
            ship.UpdateVisual(_renderOrigin, dt, frameTime);

        List<uint>? stale = null;
        foreach (var (id, seenAt) in _remoteSeen)
        {
            if (now - seenAt > 3.0 && !_remoteShips.ContainsKey(id))
                (stale ??= new List<uint>()).Add(id);
            if (now - seenAt > 3.0 && _remoteShips.TryGetValue(id, out var view))
            {
                view.QueueFree();
                _remoteShips.Remove(id);
                (stale ??= new List<uint>()).Add(id);
            }
        }

        if (stale is not null)
        {
            foreach (var id in stale)
                _remoteSeen.Remove(id);
        }
    }

    private void UpdateSector()
    {
        if (_playerShip is null || !_playerShip.Track.HasData)
            return;

        var sector = _catalog.SectorAt(_playerShip.Track.WorldPosition);
        var outside = Vec3d.Distance(_playerShip.Track.WorldPosition, sector.Center) > sector.Radius;
        var id = outside ? "deep-space" : sector.Id;
        var name = outside ? Locale.Text("deep_space") : sector.Name;
        if (id == _sectorId)
            return;

        _sectorId = id;
        if (_net?.State == ConnectionState.Connected)
            _hud.ShowToast(Locale.Format("sector_entered", name.ToUpperInvariant()),
                new Color(0.55f, 0.85f, 1.0f), 3.5f);
    }

    private void UpdateTactical()
    {
        if (!_tactical.Visible)
            return;

        var playerPosition = _playerShip?.Track.WorldPosition ?? Vec3d.Zero;
        _tactical.PlayerPosition = playerPosition;
        var velocity = _playerShip?.Track.Velocity ?? Vector3.Zero;
        _tactical.PlayerVelocity = new Vec3d(velocity.X, velocity.Y, velocity.Z);
        _tactical.PlayerYaw = _playerShip?.Track.Yaw ?? 0;
        _tactical.PlayerId = _playerId;
        _tactical.TargetId = _targetId;
        _tactical.HasTarget = _hasTarget;
        _tactical.OrbitTargetId = _playerShip?.Track.OrbitTargetId ?? 0;
        _tactical.OrbitAltitude = _playerShip?.Track.OrbitAltitude ?? 0;
        _tactical.ApproachTargetId = _playerShip?.Track.ApproachTargetId ?? 0;
        _tactical.ApproachPoint = _playerShip?.Track.WarpDestination ?? _playerShip?.Track.ApproachPoint;

        _tactical.Contacts.Clear();
        foreach (var ship in _remoteShips.Values.Where(s => s.Track.HasData))
        {
            _tactical.Contacts.Add(new TacticalContact
            {
                Id = ship.Id,
                Name = ship.DisplayName,
                Kind = ship.Kind == EntityKind.Player ? "PLAYER" : "NPC",
                Position = ship.Track.WorldPosition,
                Color = ship.Kind == EntityKind.Player ? new Color(0.55f, 0.95f, 0.7f) : new Color(1.0f, 0.66f, 0.3f),
                IsPlayer = ship.Kind == EntityKind.Player,
            });
        }

        _tactical.QueueRedraw();
    }

    private void UpdateCameraClipping()
    {
        var far = _worldBuilder?.RequiredFarPlane ?? 900_000f;
        _camera.Far = far;
        // Keep float32 frustum extraction finite when a nearby planet fills the view.
        _camera.Near = Math.Max(_mode == ViewMode.Cockpit ? .05f : .35f, far / 8_000_000f);
    }

    private void UpdateCamera(float dt)
    {
        if (_playerShip is null || !_playerShip.Track.HasData)
            return;

        var transform = _playerShip.GlobalTransform;
        var position = transform.Origin;
        var basis = transform.Basis;
        var forward = -basis.Z;
        var up = basis.Y;
        var right = basis.X;

        switch (_mode)
        {
            case ViewMode.Cockpit:
            {
                var local = CockpitView.EyePosition;
                var camPos = position + basis * local;
                if (!LookingAround) _cockpitLook = Vector2.Zero;
                var look = _cockpitLook;
                _camera.GlobalTransform = new Transform3D(basis * new Basis(
                    Quaternion.FromEuler(new Vector3(look.Y, look.X, 0))), camPos);
                _camera.Fov = 78 + _warpEffect.Intensity * 16;
                break;
            }
            case ViewMode.External:
            {
                var orbit = basis * new Basis(Quaternion.FromEuler(new Vector3(_cameraOrbit.Y, _cameraOrbit.X, 0)));
                var targetRotation = orbit.GetRotationQuaternion();
                var lookAt = position;
                if (_cameraSnap)
                {
                    _chaseRotation = targetRotation;
                    _cameraSnap = false;
                }
                else
                {
                    var lerp = 1f - Mathf.Exp(-7.5f * dt);
                    _chaseRotation = _chaseRotation.Slerp(targetRotation, lerp).Normalized();
                }

                _cameraDistance = Mathf.Lerp(_cameraDistance, _targetCameraDistance, 1f - Mathf.Exp(-10f * dt));
                var offset = new Basis(_chaseRotation) * new Vector3(1, 9, 34).Normalized() * _cameraDistance;
                _camera.GlobalPosition = position + offset;
                _camera.LookAt(lookAt, up);
                var targetFov = (_playerShip.Boosting ? 82f : 70f) + _warpEffect.Intensity * 20;
                _camera.Fov = Mathf.Lerp(_camera.Fov, targetFov, 1f - Mathf.Exp(-4f * dt));
                break;
            }
        }
    }

    private void UpdateHud(float dt)
    {
        var speed = 0f;
        var throttle = 0f;
        var nearest=SurfaceNavigation.Nearest(_catalog,_catalog.LocalFrameOrigin+(_playerShip?.Track.WorldPosition??Vec3d.Zero));
        var surfaceInfo=Locale.Format("surface_clearance",nearest.Body.Name,NavigationPanel.Distance(nearest.DistanceMetres));
        var docked = false;
        var assist = false;
        var autopilot = false;
        var following = false;

        if (_playerShip is not null && _playerShip.Track.HasData)
        {
            var track = _playerShip.Track;
            speed = track.Velocity.Length();
            throttle = track.Throttle;
            docked = track.Docked;
            assist = track.FlightAssist;
            autopilot = track.ApproachTargetId != 0 || track.OrbitTargetId != 0;
            following = track.ApproachFollowing;
            _hud.SetTelemetry(speed, throttle, surfaceInfo, _playerShip.Boosting, 0, _scanRange / 1000f,
                autopilot, assist, following, track.Cruising, _cruise, track.CruiseBraking);
        }

        ContactRef? contact = null;
        if (_hasTarget)
        {
            contact = FindContact(_targetId);
            if (contact is null)
                _hasTarget = false;
        }

        Vector2? velocityPoint = null;
        if (_playerShip is not null && _mode != ViewMode.Tactical && _playerShip.Track.Velocity.Length() > 0.5f)
        {
            var ahead = _camera.GlobalPosition + _playerShip.Track.Velocity.Normalized() * 1000;
            if (!_camera.IsPositionBehind(ahead)) velocityPoint = _camera.UnprojectPosition(ahead);
        }
        Vector2? nosePoint = null;
        if (_playerShip is not null && _mode != ViewMode.Tactical)
        {
            var nose = _camera.GlobalPosition - _playerShip.GlobalBasis.Z * 1000;
            if (!_camera.IsPositionBehind(nose)) nosePoint = _camera.UnprojectPosition(nose);
        }
        _hud.SetFlightDirections(nosePoint, velocityPoint);
        if (_cockpit is not null && _mode == ViewMode.Cockpit)
        {
            if (_playerShip is not null)
                _cockpit.UpdateFlight(_playerShip.GlobalBasis.Inverse() * _playerShip.Track.Velocity,
                    _playerShip.GlobalBasis.GetEuler(), _input.Brake, _input.Boost, _playerShip.Track.WarpStage != WarpStage.None);
            _cockpit.UpdateInstruments(speed, throttle, docked,
                contact?.Name, contact?.Distance ?? 0, nearest.Body.Name, nearest.DistanceMetres,
                contact is not null, assist, autopilot, following, _playerShip?.Track.WarpStage ?? WarpStage.None);
        }
    }

    private Vec3d PlayerPosition() =>
        _playerShip is not null && _playerShip.Track.HasData ? _playerShip.Track.WorldPosition : Vec3d.Zero;

    private void UpdateRenderOrigin(double frameTime)
    {
        if (_playerShip is null || !_playerShip.Track.HasData)
            return;
        _playerShip.Track.Sample(frameTime - 0.05, _renderOrigin);
        var position = _playerShip.Track.WorldPosition;
        var next = RenderFrame.ChooseOrigin(position, _renderOrigin);
        if (next.Equals(_renderOrigin))
            return;
        var shift = ShipTrack.ToVector3(next - _renderOrigin);
        _camera.GlobalPosition -= shift;
        _renderOrigin = next;
        _staticRoot.Position = -ShipTrack.ToVector3(_renderOrigin);
    }

    private void OnTransportError(string message) => _hud.ShowToast(message, new Color(1f, 0.45f, 0.4f), 6f);
}
