using System;
using Godot;

namespace IndifferentStars.Client;

/// <summary>Common HUD for all three representations.</summary>
public sealed partial class Hud : CanvasLayer
{
    private static readonly Color Cyan = new(0.55f, 0.85f, 1.0f);
    private static readonly Color Warm = new(1.0f, 0.72f, 0.35f);
    private static readonly Color Green = new(0.45f, 0.92f, 0.65f);
    private static readonly Color Red = new(1.0f, 0.42f, 0.38f);
    private static readonly Color Dim = new(0.55f, 0.63f, 0.72f);

    private Control _root = null!;
    private Panel _connectionPanel = null!;
    private Label _connectionLabel = null!;
    private Label _titleLabel = null!;
    private Label _systemLabel = null!;
    private Label _modeLabel = null!;
    private Label _targetLabel = null!;
    private Label _targetDetailLabel = null!;
    private Label _speedLabel = null!;
    private Label _telemetryLabel = null!;
    private Label _warpLabel = null!;
    private ProgressBar _throttleBar = null!;
    private Label _toastLabel = null!;
    private Panel _toastPanel = null!;
    private Panel _scanPanel = null!;
    private Label _scanTitle = null!;
    private Label _scanBody = null!;
    private ReticleControl _reticle = null!;
    private Control _overlay = null!;
    private Label _overlayLabel = null!;
    private Label _overlayHint = null!;

    private float _toastTime;
    private float _toastDuration;
    private float _scanTime;
    private float _pulse;

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _reticle = new ReticleControl();
        _root.AddChild(_reticle);

        BuildTopLeft();
        BuildTopRight();
        BuildBottomLeft();
        BuildTelemetry();
        BuildScanPanel();
        BuildToast();
        BuildOverlay();

        _modeLabel = MakeLabel("EXTERNAL 3D", 15, Cyan);
        Place(_modeLabel, 0.5f, 0, new Vector2(-130, 22), new Vector2(260, 24));
        _modeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _modeLabel.Text = "EXTERNAL 3D";
        _root.AddChild(_modeLabel);

        SetConnection(ConnectionState.Connecting, "Connecting…", -1);
    }

    public override void _Process(double delta)
    {
        _pulse += (float)delta;
        if (_toastTime > 0)
        {
            _toastTime -= (float)delta;
            var alpha = Mathf.Clamp(_toastTime / Mathf.Min(0.5f, _toastDuration), 0, 1);
            _toastPanel.Modulate = new Color(1, 1, 1, alpha);
            if (_toastTime <= 0)
                _toastPanel.Visible = false;
        }

        if (_scanTime > 0)
        {
            _scanTime -= (float)delta;
            if (_scanTime <= 0)
                _scanPanel.Visible = false;
        }

        if (_overlay.Visible)
            _overlayLabel.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * Mathf.Sin(_pulse * 2.2f));
    }

    public void SetConnection(ConnectionState state, string text, int ping)
    {
        _connectionLabel.Text = text.ToUpperInvariant();
        _connectionLabel.AddThemeColorOverride("font_color",
            state == ConnectionState.Connected ? Green : state == ConnectionState.Rejected ? Red : Warm);
        _connectionLabel.Text = state switch
        {
            ConnectionState.Connected => $"LINK ONLINE   {ping} ms",
            ConnectionState.Rejected => $"LINK FAILED   {text}".ToUpperInvariant(),
            _ => "LINK: CONTACTING SERVER…",
        };

        _overlay.Visible = state != ConnectionState.Connected;
        if (state != ConnectionState.Connected)
        {
            _overlayLabel.Text = state == ConnectionState.Rejected ? "CONNECTION FAILED" : "ESTABLISHING LINK";
            _overlayLabel.AddThemeColorOverride("font_color", state == ConnectionState.Rejected ? Red : Cyan);
            _overlayHint.Text = text + "\n\nStart the server with:\n  tools/run-server.sh\n\nThen the client reconnects automatically.";
        }
    }

    public void SetSystem(string systemName, int seed, uint shipId)
    {
        _titleLabel.Text = "INDIFFERENT STARS  //  FIRST FLIGHT";
        _systemLabel.Text = $"{systemName.ToUpperInvariant()}\nSEED {seed}   SHIP #{shipId}";
    }

    public void SetMode(string mode, string subtitle)
    {
        _modeLabel.Text = mode;
        _reticle.Visible = mode != "2D NAVIGATION";
        _reticle.ReticleColor = mode == "COCKPIT" ? new Color(1.0f, 0.75f, 0.4f, 0.7f) : new Color(0.55f, 0.85f, 1.0f, 0.72f);
        _reticle.QueueRedraw();
        _targetDetailLabel.Text = subtitle;
    }

    public void SetTelemetry(float speed, float throttle, float altitude, bool boosting, float warpCooldown, float scanRangeKm, bool autopilot)
    {
        _speedLabel.Text = $"{speed,5:F0} m/s";
        _speedLabel.AddThemeColorOverride("font_color", boosting ? Warm : Cyan);
        _throttleBar.Value = (throttle + 1f) * 50f;
        _throttleBar.AddThemeStyleboxOverride("fill", MakeBarFill(boosting ? Warm : Cyan));
        var warp = warpCooldown <= 0 ? "READY" : $"{warpCooldown:F1}s";
        _telemetryLabel.Text =
            $"THROTTLE {(throttle >= 0 ? "+" : "")}{throttle * 100:F0}%    ALT {(altitude >= 0 ? "+" : "")}{altitude:F0} m\n" +
            $"SCAN RANGE {scanRangeKm:F1} km    WARP {warp}    AUTOPILOT {(autopilot ? "ON" : "OFF")}";
        _warpLabel.Text = boosting ? "BOOST" : "";
    }

    public void SetTarget(string? name, string type, float distanceMeters, float relativeAltitude, bool inRange, bool hasTarget)
    {
        if (!hasTarget)
        {
            _targetLabel.Text = "NO CONTACT SELECTED";
            _targetLabel.AddThemeColorOverride("font_color", Dim);
            _targetDetailLabel.Text = "TAB cycles nearby contacts";
            return;
        }

        _targetLabel.Text = name?.ToUpperInvariant() ?? "UNKNOWN CONTACT";
        _targetLabel.AddThemeColorOverride("font_color", inRange ? Green : Red);
        var alt = relativeAltitude >= 0 ? "+" : "";
        _targetDetailLabel.Text =
            $"{type.ToUpperInvariant()}\n" +
            $"RANGE {distanceMeters / 1000f:F2} km    ALT {alt}{relativeAltitude:F0} m\n" +
            (inRange ? "PRESS F TO SCAN   •   X SHORT WARP" : "OUT OF SCANNER RANGE");
    }

    public void SetScanPending(string targetName)
    {
        _scanPanel.Visible = true;
        _scanTime = 5;
        _scanTitle.Text = "SCANNING…";
        _scanTitle.AddThemeColorOverride("font_color", Warm);
        _scanBody.Text = targetName;
    }

    public void ShowScanResult(string title, string body, bool success)
    {
        _scanPanel.Visible = true;
        _scanTime = 16;
        _scanTitle.Text = title.ToUpperInvariant();
        _scanTitle.AddThemeColorOverride("font_color", success ? Green : Red);
        _scanBody.Text = body;
    }

    public void ShowToast(string text, Color? color = null, float duration = 4.0f)
    {
        _toastPanel.Visible = true;
        _toastLabel.Text = text;
        _toastLabel.AddThemeColorOverride("font_color", color ?? Cyan);
        _toastTime = duration;
        _toastDuration = duration;
        _toastPanel.Modulate = Colors.White;
    }

    public void SetQuality(bool high) => ShowToast(high ? "QUALITY: HIGH (bloom + atmosphere)" : "QUALITY: LOW (bloom off)", Dim, 2.5f);

    private void BuildTopLeft()
    {
        _connectionPanel = CreatePanel();
        Place(_connectionPanel, 0, 0, new Vector2(24, 24), new Vector2(430, 132));
        _root.AddChild(_connectionPanel);

        _connectionLabel = MakeLabel("LINK: CONTACTING SERVER…", 13, Warm);
        Place(_connectionLabel, 0, 0, new Vector2(16, 12), new Vector2(398, 20));
        _connectionPanel.AddChild(_connectionLabel);

        _titleLabel = MakeLabel("INDIFFERENT STARS  //  FIRST FLIGHT", 19, Cyan);
        Place(_titleLabel, 0, 0, new Vector2(16, 36), new Vector2(398, 26));
        _connectionPanel.AddChild(_titleLabel);

        _systemLabel = MakeLabel("VESPER\nSEED —", 13, Dim);
        Place(_systemLabel, 0, 0, new Vector2(16, 64), new Vector2(398, 54));
        _connectionPanel.AddChild(_systemLabel);
    }

    private void BuildTopRight()
    {
        var panel = CreatePanel();
        Place(panel, 1, 0, new Vector2(-464, 24), new Vector2(440, 154));
        _root.AddChild(panel);

        var header = MakeLabel("SENSOR CONTACT", 12, Dim);
        Place(header, 0, 0, new Vector2(16, 10), new Vector2(408, 18));
        panel.AddChild(header);

        _targetLabel = MakeLabel("NO CONTACT SELECTED", 19, Dim);
        Place(_targetLabel, 0, 0, new Vector2(16, 30), new Vector2(408, 28));
        panel.AddChild(_targetLabel);

        _targetDetailLabel = MakeLabel("TAB cycles nearby contacts", 13, Dim);
        _targetDetailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(_targetDetailLabel, 0, 0, new Vector2(16, 62), new Vector2(408, 80));
        panel.AddChild(_targetDetailLabel);
    }

    private void BuildBottomLeft()
    {
        var panel = CreatePanel();
        Place(panel, 0, 1, new Vector2(24, -154), new Vector2(500, 130));
        _root.AddChild(panel);

        var controls = MakeLabel(
            "[W/S] THRUST   [A/D] STRAFE   [ARROWS/MOUSE] TURN\n" +
            "[Q/E] ROLL   [R/C] VERTICAL   [SHIFT] BOOST   [SPACE] BRAKE\n" +
            "[TAB] CONTACT    [F] SCAN    [G] APPROACH    [X] WARP\n" +
            "[F1] 2D NAV      [F2] EXTERNAL 3D  [F3] COCKPIT  [F4] QUALITY",
            13, Dim);
        Place(controls, 0, 0, new Vector2(16, 12), new Vector2(468, 106));
        panel.AddChild(controls);
    }

    private void BuildTelemetry()
    {
        var panel = CreatePanel();
        Place(panel, 1, 1, new Vector2(-404, -154), new Vector2(380, 130));
        _root.AddChild(panel);

        _speedLabel = MakeLabel("0 m/s", 34, Cyan);
        Place(_speedLabel, 0, 0, new Vector2(16, 8), new Vector2(220, 46));
        panel.AddChild(_speedLabel);

        _warpLabel = MakeLabel("", 15, Warm);
        Place(_warpLabel, 1, 0, new Vector2(-120, 20), new Vector2(100, 22));
        _warpLabel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.AddChild(_warpLabel);

        _throttleBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 50,
            ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _throttleBar.AddThemeStyleboxOverride("background", MakeBarBackground());
        _throttleBar.AddThemeStyleboxOverride("fill", MakeBarFill(Cyan));
        Place(_throttleBar, 0, 0, new Vector2(16, 56), new Vector2(348, 7));
        panel.AddChild(_throttleBar);

        _telemetryLabel = MakeLabel("THROTTLE 0%   ALT +0 m\nSCAN RANGE 4.2 km   WARP READY", 13, Dim);
        Place(_telemetryLabel, 0, 0, new Vector2(16, 70), new Vector2(348, 50));
        panel.AddChild(_telemetryLabel);
    }

    private void BuildScanPanel()
    {
        _scanPanel = CreatePanel();
        Place(_scanPanel, 0.5f, 1, new Vector2(-340, -352), new Vector2(680, 180));
        _scanPanel.Visible = false;
        _root.AddChild(_scanPanel);

        _scanTitle = MakeLabel("SCAN", 17, Green);
        Place(_scanTitle, 0, 0, new Vector2(18, 12), new Vector2(644, 24));
        _scanPanel.AddChild(_scanTitle);

        _scanBody = MakeLabel("", 14, new Color(0.8f, 0.87f, 0.94f));
        _scanBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(_scanBody, 0, 0, new Vector2(18, 42), new Vector2(644, 128));
        _scanPanel.AddChild(_scanBody);
    }

    private void BuildToast()
    {
        _toastPanel = CreatePanel();
        Place(_toastPanel, 0.5f, 0.5f, new Vector2(-300, 110), new Vector2(600, 42));
        _toastPanel.Visible = false;
        _root.AddChild(_toastPanel);

        _toastLabel = MakeLabel("", 15, Cyan);
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        Place(_toastLabel, 0, 0, new Vector2(8, 9), new Vector2(584, 24));
        _toastPanel.AddChild(_toastLabel);
    }

    private void BuildOverlay()
    {
        _overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_overlay);

        var shade = new ColorRect { Color = new Color(0.002f, 0.004f, 0.01f, 0.72f) };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shade.MouseFilter = Control.MouseFilterEnum.Ignore;
        _overlay.AddChild(shade);

        _overlayLabel = MakeLabel("ESTABLISHING LINK", 34, Cyan);
        _overlayLabel.HorizontalAlignment = HorizontalAlignment.Center;
        Place(_overlayLabel, 0.5f, 0.5f, new Vector2(-300, -110), new Vector2(600, 46));
        _overlay.AddChild(_overlayLabel);

        _overlayHint = MakeLabel("", 15, Dim);
        _overlayHint.HorizontalAlignment = HorizontalAlignment.Center;
        _overlayHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(_overlayHint, 0.5f, 0.5f, new Vector2(-320, -54), new Vector2(640, 180));
        _overlay.AddChild(_overlayHint);
    }

    private Panel CreatePanel()
    {
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.022f, 0.04f, 0.74f),
            BorderColor = new Color(0.35f, 0.62f, 0.8f, 0.34f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 4,
            ContentMarginRight = 4,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static StyleBoxFlat MakeBarBackground() => new()
    {
        BgColor = new Color(0.2f, 0.3f, 0.38f, 0.35f),
        CornerRadiusTopLeft = 2,
        CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2,
        CornerRadiusBottomRight = 2,
    };

    private static StyleBoxFlat MakeBarFill(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 2,
        CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2,
        CornerRadiusBottomRight = 2,
    };

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private static void Place(Control control, float anchorX, float anchorY, Vector2 offset, Vector2 size)
    {
        control.AnchorLeft = anchorX;
        control.AnchorRight = anchorX;
        control.AnchorTop = anchorY;
        control.AnchorBottom = anchorY;
        control.OffsetLeft = offset.X;
        control.OffsetTop = offset.Y;
        control.OffsetRight = offset.X + size.X;
        control.OffsetBottom = offset.Y + size.Y;
    }
}
