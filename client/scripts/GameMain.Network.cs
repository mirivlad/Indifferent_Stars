using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;

namespace IndifferentStars.Client;

public sealed partial class GameMain
{
    private readonly record struct ContactRef(
        uint Id,
        string Name,
        string Type,
        Vector3 Position,
        float Radius,
        bool IsShip,
        float Distance,
        float RelativeAltitude);

    private ViewMode _requestedInitialView = ViewMode.External;
    private bool _hasRequestedInitialView;
    private string? _captureDir;
    private int _captureStep;
    private float _captureTimer;

    private void ParseCommandLine()
    {
        var server = System.Environment.GetEnvironmentVariable("IS_SERVER") ?? _settings.Server;
        var envName = System.Environment.GetEnvironmentVariable("IS_NAME") ?? _settings.Callsign;
        var envQuality = System.Environment.GetEnvironmentVariable("IS_QUALITY");

        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server" when i + 1 < args.Length:
                    server = args[++i];
                    break;
                case "--name" when i + 1 < args.Length:
                    envName = args[++i];
                    break;
                case "--quality" when i + 1 < args.Length:
                    envQuality = args[++i];
                    break;
                case "--capture" when i + 1 < args.Length:
                    _captureDir = Path.GetFullPath(args[++i]);
                    break;
                case "--view" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "2d" or "tactical":
                            _requestedInitialView = ViewMode.Tactical;
                            break;
                        case "cockpit":
                            _requestedInitialView = ViewMode.Cockpit;
                            break;
                        default:
                            _requestedInitialView = ViewMode.External;
                            break;
                    }
                    _hasRequestedInitialView = true;
                    break;
                case "--help" or "-h":
                    GD.Print("Indifferent Stars client options (after --):\n" +
                             "  --server host:port   default 127.0.0.1:7777\n" +
                             "  --name <name>        callsign shown to other clients\n" +
                             "  --view 2d|3d|cockpit initial representation\n" +
                             "  --quality high|low   bloom/effects preset\n" +
                             "  --capture <dir>      save three screenshots and quit");
                    break;
            }
        }

        var split = server.Split(':', 2);
        _serverHost = split[0];
        if (split.Length > 1 && int.TryParse(split[1], out var port))
            _serverPort = port;

        _highQuality = envQuality is null ? _settings.Glow : !string.Equals(envQuality, "low", StringComparison.OrdinalIgnoreCase);
        _settings.Glow = _highQuality;
        _playerName = string.IsNullOrWhiteSpace(envName)
            ? $"Captain-{new Random().Next(101, 999)}"
            : envName!.Trim();
        _settings.Server = server;
        _settings.Callsign = _playerName;

        if (_captureDir is not null)
        {
            DirAccess.MakeDirRecursiveAbsolute(_captureDir);
            GD.Print($"Capture mode: {_captureDir}");
        }
    }

    private InputState ReadInput()
    {
        if (_cruiseStopping && (_playerShip?.Track.Velocity.Length() ?? 0) < 0.1f) _cruiseStopping = false;
        if (_settingsMenu.IsOpen || _playerShip?.Track.Docked == true || (_playerShip is not null && _playerShip.Track.WarpStage != WarpStage.None))
            return InputState.None with { FlightAssist = _flightAssist };
        if (_radio.IsTyping || _navigation.IsTyping || _navigation.IsModal || _mode == ViewMode.Tactical)
            return InputState.None with { Cruise = _cruise, Brake = _cruiseStopping, FlightAssist = _flightAssist };
        var throttle = (_settings.Pressed("forward") ? 1f : 0f) - (_settings.Pressed("reverse") ? 1f : 0f);
        var steer = (_settings.Pressed("yaw_left") ? 1f : 0f) - (_settings.Pressed("yaw_right") ? 1f : 0f);
        var pitch = (_settings.Pressed("pitch_up") ? 1f : 0f) - (_settings.Pressed("pitch_down") ? 1f : 0f);
        if (!LookingAround && _mode != ViewMode.Tactical && Input.MouseMode == Input.MouseModeEnum.Captured && Time.GetTicksMsec() - _mouseTurnAt < 80)
        {
            steer = Mathf.Clamp(steer - _mouseTurn.X * _settings.MouseSensitivity * (_settings.InvertX ? -1 : 1), -1, 1);
            pitch = Mathf.Clamp(pitch - _mouseTurn.Y * _settings.MouseSensitivity * (_settings.InvertY ? -1 : 1), -1, 1);
        }
        var vertical = (_settings.Pressed("up") ? 1f : 0f) - (_settings.Pressed("down") ? 1f : 0f);
        var strafe = (_settings.Pressed("right") ? 1f : 0f) - (_settings.Pressed("left") ? 1f : 0f);
        var roll = (_settings.Pressed("roll_left") ? 1f : 0f) - (_settings.Pressed("roll_right") ? 1f : 0f);
        var boost = _settings.Pressed("boost");
        var brake = _settings.Pressed("brake");
        return new InputState(throttle, steer, pitch, vertical, boost, brake || _cruiseStopping, strafe, roll, _flightAssist, _cruise);
    }

    private void OnWelcome(HelloAcceptedMessage welcome)
    {
        _playerId = welcome.PlayerId;
        _scanRange = welcome.ScanRange;

        if (_catalog.Seed != welcome.WorldSeed)
            RebuildStaticWorld(welcome.WorldSeed);

        var createShip = _playerShip is null || _playerShip.Id != welcome.PlayerId;
        if (createShip)
        {
            _routePoints.Clear(); _confirmedRoute.Clear();
            _routeRevision=0; _confirmedRevision=0; _routeIndex=0; _routeEnabled=false;
            if (_playerShip is not null)
                _playerShip.QueueFree();

            if (_remoteShips.Remove(welcome.PlayerId, out var staleView))
                staleView.QueueFree();

            _playerShip = new ShipView(welcome.PlayerId, true, welcome.PlayerName, _highQuality);
            _playerShip.ImpactOccurred += impact =>
            {
                _impactShake=Mathf.Max(_impactShake,Mathf.Clamp(Mathf.Log(1+impact.NormalSpeed)/4.6f,0,1));
                _hud.ShowImpact(impact.NormalSpeed);
            };
            _playerShip.SetEngineEffects(_settings.EngineEffects);
            _playerShip.SetParticles(_settings.Particles);
            _dynamicRoot.AddChild(_playerShip);
            _cockpit = new CockpitView { Visible = false };
            _playerShip.AddChild(_cockpit);
            _cockpit.Visible = _mode == ViewMode.Cockpit;
            _playerShip.SetExternalModelVisible(_mode != ViewMode.Cockpit);
            _cameraSnap = true;
        }

        _playerShip!.DisplayName = welcome.PlayerName;
        _playerShip.Track.Reset(welcome.Spawn, welcome.SpawnYaw, 0);
        _tactical.CenterOnPlayer();
        _targetId = (uint)_catalog.Station.Id;
        _hasTarget = true;

        _hud.SetSystem(welcome.SystemName);
        var spawnSector = _catalog.SectorAt(welcome.Spawn);
        _sectorId = spawnSector.Id;
        _hud.ShowToast(Locale.Format("connected_welcome", _catalog.SystemName.ToUpperInvariant()), new Color(0.45f, 0.95f, 0.65f), 4f);
        GD.Print($"Connected as ship #{welcome.PlayerId} '{welcome.PlayerName}'.");

        SetMode(_mode);
        if (_hasRequestedInitialView)
            SetMode(_requestedInitialView);
    }

    private void OnJoinRejected(HelloRejectedMessage rejected)
    {
        _hud.ShowToast($"{Locale.Text("join_rejected")}: {rejected.Reason}", new Color(1f, 0.45f, 0.4f), 8f);
        GD.PrintErr($"Join rejected: {rejected.Reason}");
    }

    private void OnSnapshot(SnapshotMessage snapshot)
    {
        if (_playerShip is null)
            return;

        _serverTick = snapshot.Tick;
        var now = _net?.LastSnapshotReceivedAtSeconds ?? Time.GetTicksUsec() / 1_000_000.0;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Id == _playerId)
            {
                if (_playerShip.Track.Push(entity, now, snapshot.ServerTimeMs) && _cruise &&
                    (_playerShip.Track.CruiseInhibited || _playerShip.Track.CruiseBraking))
                {
                    _cruise = false;
                    _cruiseStopping = false;
                    _hud.ShowToast(Locale.Text(_playerShip.Track.CruiseBraking ? "cruise_safety_stop_hint" : "cruise_denied_hint"), duration: 6);
                }
                continue;
            }

            if (!_remoteShips.TryGetValue(entity.Id, out var view))
            {
                view = new ShipView(entity.Id, false, entity.Name, _highQuality);
                view.SetEngineEffects(_settings.EngineEffects);
                view.SetParticles(_settings.Particles);
                _remoteRoot.AddChild(view);
                _remoteShips[entity.Id] = view;
                GD.Print($"Tracked {(entity.Kind == EntityKind.Player ? "player" : "NPC")} {entity.Name} #{entity.Id}.");
            }

            view.DisplayName = entity.Name;
            view.Track.Push(entity, now, snapshot.ServerTimeMs);
            _remoteSeen[entity.Id] = now;
        }
    }

    private void OnScanCompleted(ScanResultMessage result)
    {
        _audio.NavigationCue();
        _hud.ShowScanResult(_settings.Language == "ru" ? result.RussianTitle : result.Title,
            _settings.Language == "ru" ? result.RussianBody : result.Body, result.Success);
        _hud.ShowToast(Locale.Text(result.Success ? "scan_complete" : "scan_rejected"),
            result.Success ? new Color(0.45f, 0.95f, 0.65f) : new Color(1f, 0.5f, 0.4f), 2.6f);
        GD.Print($"Scan #{result.TargetId}: {result.Title}");
    }

    private void OnWarpCompleted(WarpResultMessage result)
    {
        _audio.NavigationCue();
        var text = _settings.Language == "ru" ? result.RussianMessage : result.Message;
        if (result.Success && _playerShip is not null)
        {
            _cruise = false;
            _cruiseStopping = false;
            _cameraSnap = true;
            _hud.ShowToast(text, new Color(1f, 0.78f, 0.4f), 5f);
        }
        else
        {
            _hud.ShowToast(text, new Color(1f, 0.5f, 0.4f), 4.5f);
        }

        GD.Print($"Warp result: {result.Message}");
    }

    private void OnApproachCompleted(ApproachResultMessage result)
    {
        _audio.NavigationCue();
        _hud.ShowToast(Locale.Text(result.Accepted ? "autopilot_on" : "approach_unavailable"),
            result.Accepted ? new Color(0.45f, 0.95f, 0.65f) : new Color(1f, 0.5f, 0.4f), 3f);
    }

    private void OnDockCompleted(DockResultMessage result)
    {
        _audio.NavigationCue();
        var message = result.Accepted
            ? Locale.Text(result.Undock ? "undock_approved" : "dock_approved")
            : Locale.Text("dock_rejected");
        _hud.ShowToast(message, result.Accepted ? new Color(0.45f, 0.95f, 0.65f) : new Color(1f, 0.5f, 0.4f), 3f);
        if (result.Accepted && !result.Undock)
        {
            _marketRetry = 0;
            _net?.RequestMarket();
        }
    }

    private void OnMarketReceived(MarketStateMessage market)
    {
        _stationView.UpdateMarket(market.State);
    }

    private void OnTradeCompleted(TradeResultMessage result)
    {
        var text = _settings.Language == "ru" ? result.RussianMessage : result.Message;
        _stationView.UpdateMarket(result.State);
        _stationView.ShowStatus(text, result.Success);
        _hud.ShowToast(result.Success ? Locale.Text("trade_complete") : Locale.Text("trade_failed"),
            result.Success ? new Color(0.45f, 0.95f, 0.65f) : new Color(1f, 0.5f, 0.4f), 3f);
    }

    private void RequestDock()
    {
        if (_hasTarget)
            _net?.RequestDock(_targetId, false);
    }

    private void RequestApproach(bool follow = false)
    {
        if (_hasTarget)
            _net?.RequestApproach(_targetId, follow);
    }

    private void UpdateTargeting()
    {
        if (_hasTarget && FindContact(_targetId) is null)
            _hasTarget = false;
    }

    private void RequestScan()
    {
        if (_scanCooldown > 0 || _net?.IsScanPending == true)
            return;
        if (!_hasTarget || FindContact(_targetId) is not { } contact)
        {
            _hud.ShowToast(Locale.Text("choose_contact"), new Color(1f, 0.75f, 0.4f), 2.5f);
            return;
        }

        if (_net?.State != ConnectionState.Connected)
        {
            _hud.ShowToast(Locale.Text("no_connection"), new Color(1f, 0.5f, 0.4f), 2.5f);
            return;
        }

        _scanCooldown = 0.35f;
        _net.RequestScan(_targetId);
        _hud.SetScanPending(contact.Name);
    }

    private void RequestWarp()
    {
        if (!_hasTarget || FindContact(_targetId) is not { } contact)
        {
            _hud.ShowToast(Locale.Text("choose_contact"), new Color(1f, 0.75f, 0.4f), 2.5f);
            return;
        }

        if (_net?.State != ConnectionState.Connected)
        {
            _hud.ShowToast(Locale.Text("no_connection"), new Color(1f, 0.5f, 0.4f), 2.5f);
            return;
        }

        _net.RequestWarp(_targetId);
        _hud.ShowToast(Locale.Format("warp_spooling", contact.Name), new Color(1f, 0.78f, 0.4f), 2.2f);
    }

    private ContactRef? FindContact(uint id)
    {
        var player = PlayerPosition();

        if (_catalog.Find((int)id) is { } body)
            return MakeContact(body, player);

        if (_catalog.FindCelestial((int)id) is { } celestial)
        {
            var local = celestial.SystemPosition - _catalog.LocalFrameOrigin;
            var observer = _playerShip?.Track.WorldPosition ?? Vec3d.Zero;
            return new ContactRef(id, celestial.Name, celestial.Kind.ToString(), ShipTrack.ToVector3(local),
                (float)celestial.Radius, false, (float)Math.Max(0, Vec3d.Distance(observer, local) - celestial.Radius),
                (float)(local.Y - observer.Y));
        }

        if (_remoteShips.TryGetValue(id, out var ship) && ship.Track.HasData)
        {
            var distance = player.DistanceTo(ship.Track.Position);
            return new ContactRef(
                id,
                ship.DisplayName,
                ship.Track.Kind == EntityKind.Player ? "PLAYER" : "NPC",
                ship.Track.Position,
                8,
                true,
                distance,
                ship.Track.Position.Y - player.Y);
        }

        return null;
    }

    private List<ContactRef> BuildContacts()
    {
        var player = PlayerPosition();
        var contacts = new List<ContactRef>(_catalog.LocalBodies.Count + _remoteShips.Count);
        foreach (var body in _catalog.LocalBodies)
            contacts.Add(MakeContact(body, player));
        foreach (var body in new[] { _catalog.Planet, _catalog.Star })
            if (FindContact((uint)body.Id) is { } celestial)
                contacts.Add(celestial);

        foreach (var ship in _remoteShips.Values.Where(s => s.Track.HasData))
        {
            contacts.Add(new ContactRef(
                ship.Id,
                ship.DisplayName,
                ship.Track.Kind == EntityKind.Player ? "PLAYER" : "NPC",
                ship.Track.Position,
                8,
                true,
                player.DistanceTo(ship.Track.Position),
                ship.Track.Position.Y - player.Y));
        }

        return contacts;
    }

    private static ContactRef MakeContact(StaticBody body, Vector3 player)
    {
        var position = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);
        var centerDistance = player.DistanceTo(position);
        var surfaceDistance = Mathf.Max(0f, centerDistance - (float)body.Radius);
        var type = body.Kind switch
        {
            BodyKind.Star => "STAR",
            BodyKind.Planet => "PLANET",
            BodyKind.Station => "STATION",
            _ => "ASTEROID",
        };
        return new ContactRef(
            (uint)body.Id,
            body.Name,
            type,
            position,
            (float)body.Radius,
            false,
            surfaceDistance,
            position.Y - player.Y);
    }

    private void UpdateCapture(float dt)
    {
        if (_captureDir is null)
            return;

        if (_captureStep == 0)
        {
            _captureTimer += dt;
            if (_playerShip is not null && _net?.State == ConnectionState.Connected && _captureTimer > 2.0)
            {
                SetMode(ViewMode.External);
                _captureStep = 1;
                _captureTimer = 0;
            }
            else if (_captureTimer > 15.0)
            {
                GD.PrintErr("Capture aborted: no server connection.");
                _ = QuitGracefully(1);
            }
            return;
        }

        _captureTimer += dt;
        switch (_captureStep)
        {
            case 1 when _captureTimer > 0.9:
                SaveScreenshot("01_external_3d.png");
                SetMode(ViewMode.Tactical);
                _captureStep = 2;
                _captureTimer = 0;
                break;
            case 2 when _captureTimer > 0.9:
                SaveScreenshot("02_tactical_2d.png");
                SetMode(ViewMode.Cockpit);
                _captureStep = 3;
                _captureTimer = 0;
                break;
            case 3 when _captureTimer > 0.9:
                SaveScreenshot("03_cockpit.png");
                _captureStep = 4;
                GD.Print("Capture complete.");
                _ = QuitGracefully();
                break;
        }
    }

    private void SaveScreenshot(string fileName)
    {
        if (_captureDir is null)
            return;

        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(_captureDir, fileName);
        var error = image.SavePng(path);
        GD.Print($"CAPTURE {path} -> {error}");
    }
}
