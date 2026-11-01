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
    private TacticalView _tactical = null!;
    private CanvasLayer _tacticalLayer = null!;
    private PopupMenu _contactMenu = null!;
    private CanvasLayer _stationLayer = null!;
    private Button _undockButton = null!;

    private readonly Dictionary<uint, ShipView> _remoteShips = new();
    private readonly Dictionary<uint, double> _remoteSeen = new();
    private ViewMode _mode = ViewMode.External;
    private bool _highQuality = true;
    private bool _cameraSnap = true;
    private uint _playerId;
    private uint _targetId;
    private bool _hasTarget;
    private float _warpCooldown;
    private float _scanCooldown;
    private float _lastHudUpdate;
    private ulong _serverTick;
    private InputState _input = InputState.None;
    private bool _flightAssist;
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

        ParseCommandLine();
        _catalog = WorldCatalog.Generate(DefaultSeed);

        _staticRoot = new Node3D { Name = "StaticWorld" };
        AddChild(_staticRoot);
        _dynamicRoot = new Node3D { Name = "DynamicWorld" };
        AddChild(_dynamicRoot);
        _remoteRoot = new Node3D { Name = "RemoteShips" };
        _dynamicRoot.AddChild(_remoteRoot);

        SetupCamera();
        SetupUi();
        RebuildStaticWorld(DefaultSeed);

        _hud.SetSystem(_catalog.SystemName, _catalog.Seed, 0);
        _hud.SetMode("EXTERNAL 3D", "Connecting to the authoritative server");
        _hud.SetTarget(null, "", 0, 0, false, false);

        _net = new NetworkClient(_serverHost, _serverPort, _playerName);
        _net.WelcomeReceived += OnWelcome;
        _net.JoinRejected += OnJoinRejected;
        _net.SnapshotReceived += OnSnapshot;
        _net.ScanCompleted += OnScanCompleted;
        _net.WarpCompleted += OnWarpCompleted;
        _net.ApproachCompleted += OnApproachCompleted;
        _net.DockCompleted += OnDockCompleted;
        _net.TransportError += OnTransportError;
        _net.Start();

        GetWindow().Title = $"Indifferent Stars — First Flight — {_playerName}";
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        GD.Print($"Indifferent Stars client: {_playerName} -> {_serverHost}:{_serverPort}");
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _net?.Poll();
        UpdateRenderOrigin();

        _input = ReadInput();
        _net?.Update(delta, _input);

        if (_playerShip is not null)
        {
            _playerShip.Boosting = _input.Boost;
            _playerShip.UpdateVisual(_renderOrigin);
            var docked = _playerShip.Track.Docked;
            if (_stationLayer.Visible != docked)
            {
                _stationLayer.Visible = docked;
                if (docked)
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    _undockButton.GrabFocus();
                }
            }
        }

        UpdateRemoteShips();
        UpdateTargeting();
        UpdateTactical();
        UpdateCamera(dt);
        _worldBuilder?.Process(delta, _playerShip?.Track.WorldPosition ?? Vec3d.Zero, _renderOrigin);
        _camera.Far = _worldBuilder?.RequiredFarPlane ?? 900_000f;

        _warpCooldown = Mathf.Max(0f, _warpCooldown - dt);
        _scanCooldown = Mathf.Max(0f, _scanCooldown - dt);
        if (_net is not null)
        {
            _hud.SetConnection(_net.State, _net.StatusText, _net.PingMs);
            if (_net.Welcome is not null && _net.State == ConnectionState.Connected)
                _serverTick = _net.LastSnapshotTick;
        }

        UpdateHud(dt);
        UpdateCapture(dt);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.F1:
                    SetMode(ViewMode.Tactical);
                    break;
                case Key.F2:
                    SetMode(ViewMode.External);
                    break;
                case Key.F3:
                    SetMode(ViewMode.Cockpit);
                    break;
                case Key.F4:
                    ToggleQuality();
                    break;
                case Key.Tab:
                    CycleTarget();
                    break;
                case Key.F:
                    RequestScan();
                    break;
                case Key.X:
                    RequestWarp();
                    break;
                case Key.G:
                    RequestApproach();
                    break;
                case Key.H:
                    RequestDock();
                    break;
                case Key.Z when _mode != ViewMode.Tactical:
                    _flightAssist = !_flightAssist;
                    break;
                case Key.Home when _mode == ViewMode.Tactical:
                    _tactical.CenterOnPlayer();
                    break;
                case Key.M when _mode == ViewMode.Tactical:
                    _tactical.ShowSystem();
                    break;
                case Key.Escape:
                    if (Input.MouseMode == Input.MouseModeEnum.Captured)
                        Input.MouseMode = Input.MouseModeEnum.Visible;
                    else
                        GetTree().Quit();
                    break;
            }
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
                    foreach (var celestial in new[] { _catalog.Star, _catalog.Planet })
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
                        _contactMenu.Position = (Vector2I)GetViewport().GetMousePosition();
                        _contactMenu.Popup();
                    }
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
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);

        _tacticalLayer = new CanvasLayer { Name = "TacticalLayer", Layer = 5 };
        AddChild(_tacticalLayer);
        _tactical = new TacticalView { Name = "TacticalView", Catalog = _catalog };
        _tacticalLayer.AddChild(_tactical);
        _contactMenu = new PopupMenu();
        _contactMenu.AddItem("Approach", 1);
        _contactMenu.AddItem("Scan", 2);
        _contactMenu.AddItem("Warp to 1 km", 3);
        _contactMenu.AddItem("Request docking", 4);
        _contactMenu.AddItem("Warp to 500 m", 5);
        _contactMenu.AddItem("Warp to 5 km", 6);
        _contactMenu.IdPressed += id =>
        {
            if (id == 1) RequestApproach();
            else if (id == 2) RequestScan();
            else if (id == 3) RequestWarp();
            else if (id == 4) RequestDock();
            else if (id == 5) RequestWarp(500);
            else if (id == 6) RequestWarp(5000);
        };
        AddChild(_contactMenu);

        _stationLayer = new CanvasLayer { Name = "StationLayer", Layer = 15, Visible = false };
        AddChild(_stationLayer);
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-240, -130);
        panel.CustomMinimumSize = new Vector2(480, 260);
        _stationLayer.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "KESTREL REACH — DOCKED", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new Label { Text = "Information: orbital station and traffic control." });
        column.AddChild(new Label { Text = "Communications: docking control online." });
        column.AddChild(new Label { Text = "Services: docking and departure." });
        column.AddChild(new Label { Text = "Trade: unavailable at this station." });
        _undockButton = new Button { Text = "Undock" };
        _undockButton.Pressed += () => _net?.RequestDock((uint)_catalog.Station.Id, true);
        column.AddChild(_undockButton);
    }

    private void RebuildStaticWorld(int seed)
    {
        _catalog = WorldCatalog.Generate(seed);
        if (_worldBuilder is not null)
        {
            _staticRoot.QueueFree();
            _staticRoot = new Node3D { Name = "StaticWorld" };
            AddChild(_staticRoot);
            MoveChild(_staticRoot, 0);
        }

        _worldBuilder = new WorldBuilder(_staticRoot, _catalog, _highQuality);
        _worldBuilder.Build();
        _tactical.Catalog = _catalog;
        GD.Print($"World built: {_catalog.SystemName}, seed {seed}, {_catalog.Asteroids.Count} asteroids.");
    }

    private void SetMode(ViewMode mode)
    {
        _mode = mode;
        if (mode == ViewMode.Tactical)
            Input.MouseMode = Input.MouseModeEnum.Visible;
        _tactical.Visible = mode == ViewMode.Tactical;
        _tacticalLayer.Visible = mode == ViewMode.Tactical;
        _staticRoot.Visible = mode != ViewMode.Tactical;
        _dynamicRoot.Visible = mode != ViewMode.Tactical;

        if (_cockpit is not null)
            _cockpit.Visible = mode == ViewMode.Cockpit;
        _playerShip?.SetExternalModelVisible(mode != ViewMode.Cockpit);

        _camera.Near = mode == ViewMode.Cockpit ? 0.05f : 0.35f;
        _camera.Far = _worldBuilder?.RequiredFarPlane ?? 900_000f;
        if (_cameraFill is not null)
            _cameraFill.Visible = mode == ViewMode.External;
        _cameraSnap = true;

        var (title, subtitle) = mode switch
        {
            ViewMode.Tactical => ("2D NAVIGATION", "Top-down tactical plot — R/C controls relative altitude"),
            ViewMode.Cockpit => ("COCKPIT", "Inside the ship — same hull, same authoritative state"),
            _ => ("EXTERNAL 3D", "Chase camera — same hull, same authoritative state"),
        };
        _hud.SetMode(title, subtitle);
        if (_playerShip is not null)
            _worldBuilder?.SetGlowEnabled(_highQuality && mode != ViewMode.Tactical);
    }

    private void ToggleQuality()
    {
        _highQuality = !_highQuality;
        _worldBuilder?.SetGlowEnabled(_highQuality && _mode != ViewMode.Tactical);
        _hud.SetQuality(_highQuality);
    }

    private void UpdateRemoteShips()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var ship in _remoteShips.Values)
            ship.UpdateVisual(_renderOrigin);

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

    private void UpdateTactical()
    {
        if (!_tactical.Visible)
            return;

        var playerPosition = _playerShip?.Track.WorldPosition ?? Vec3d.Zero;
        _tactical.PlayerPosition = playerPosition;
        _tactical.PlayerYaw = _playerShip?.Track.Yaw ?? 0;
        _tactical.PlayerAltitude = (float)playerPosition.Y;
        _tactical.PlayerId = _playerId;
        _tactical.TargetId = _targetId;
        _tactical.HasTarget = _hasTarget;
        _tactical.ApproachTargetId = _playerShip?.Track.ApproachTargetId ?? 0;

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
                _camera.GlobalTransform = new Transform3D(basis, camPos);
                _camera.Fov = 78;
                break;
            }
            case ViewMode.External:
            {
                var desired = position - forward * 17.0f + up * 5.4f + right * 1.0f;
                var lookAt = position + forward * 12.0f;
                if (_cameraSnap)
                {
                    _camera.GlobalPosition = desired;
                    _cameraSnap = false;
                }
                else
                {
                    var lerp = 1f - Mathf.Exp(-7.5f * dt);
                    _camera.GlobalPosition = _camera.GlobalPosition.Lerp(desired, lerp);
                }

                _camera.LookAt(lookAt, up);
                var targetFov = _playerShip.Boosting ? 82f : 70f;
                _camera.Fov = Mathf.Lerp(_camera.Fov, targetFov, 1f - Mathf.Exp(-4f * dt));
                break;
            }
        }
    }

    private void UpdateHud(float dt)
    {
        if (_playerShip is not null && _playerShip.Track.HasData)
        {
            var track = _playerShip.Track;
            var speed = track.Velocity.Length();
            _hud.SetTelemetry(speed, track.Throttle, track.Position.Y, _input.Boost, _warpCooldown, _scanRange / 1000f, track.ApproachTargetId != 0, track.FlightAssist);
        }

        if (!_hasTarget)
        {
            _hud.SetTarget(null, "", 0, 0, false, false);
            return;
        }

        var contact = FindContact(_targetId);
        if (contact is null)
        {
            _hasTarget = false;
            _hud.SetTarget(null, "", 0, 0, false, false);
            return;
        }

        _hud.SetTarget(contact.Value.Name, contact.Value.Type, contact.Value.Distance, contact.Value.RelativeAltitude,
            contact.Value.Distance <= _scanRange, true);
    }

    private Vector3 PlayerPosition() =>
        _playerShip is not null && _playerShip.Track.HasData ? _playerShip.Track.Position : Vector3.Zero;

    private void UpdateRenderOrigin()
    {
        if (_playerShip is null || !_playerShip.Track.HasData)
            return;
        var position = _playerShip.Track.LatestPosition;
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
