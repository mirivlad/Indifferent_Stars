using System;
using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

/// <summary>Common HUD for all three representations.</summary>
public sealed partial class Hud : CanvasLayer
{
    private static readonly Color Cyan = new(0.55f, 0.85f, 1.0f);
    private static readonly Color Warm = new(1.0f, 0.72f, 0.35f);
    private static readonly Color Green = new(0.45f, 0.92f, 0.65f);
    private static readonly Color Red = new(1.0f, 0.42f, 0.38f);
    private static readonly Color Dim = new(0.55f, 0.63f, 0.72f);

    public event Action<string>? NoticeRaised;
    public string ServerAddress { get; set; } = "";
    private Control _root = null!;
    private Panel _connectionPanel = null!;
    private Label _connectionLabel = null!;
    private Label _systemLabel = null!;
    private Label _modeLabel = null!;
    private Label _speedLabel = null!;
    private Label _telemetryLabel = null!;
    private Label _warpLabel = null!;
    private ProgressBar _throttleBar = null!;
    private ReticleControl _reticle = null!;
    private Control _overlay = null!;
    private Label _overlayLabel = null!;
    private Label _overlayHint = null!;

    private float _pulse;
    private float _impactStrength;
    private ShaderMaterial _impactMaterial = null!;
    public void ShowImpact(float speed)
    {
        _impactStrength=Mathf.Max(_impactStrength,Mathf.Clamp(Mathf.Log(1+speed)/4.6f,0,1));
        if(speed>5)ShowToast(Locale.Text("impact_hit"),Warm,1.5f);
    }
    private string _modeKey = "external";
    private ConnectionState _connectionState = ConnectionState.Connecting;
    private string _connectionText = "";
    private int _ping = -1;
    private string _systemName = "Vesper — Kestrel Reach";

    public void SetWarpState(WarpStage stage, float progress)
    {
        if (stage == WarpStage.None) return;
        _speedLabel.Text = Locale.Text("warp_engaged");
        _warpLabel.Text = Locale.Format("warp_stage_" + (int)stage, progress * 100);
        _telemetryLabel.Text = _telemetryLabel.Text.Replace(Locale.Text("warp_short") + " " + Locale.Text("ready"), Locale.Text("warp_engaged"));
    }

    public void SetFlightDirections(Vector2? nose, Vector2? point)
    {
        _reticle.NosePoint = nose;
        _reticle.VelocityPoint = point;
        _reticle.QueueRedraw();
    }

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _reticle = new ReticleControl();
        _root.AddChild(_reticle);

        BuildTopLeft();
        BuildTelemetry();
        BuildOverlay();
        _impactMaterial=new ShaderMaterial {Shader=GD.Load<Shader>("res://assets/shaders/impact_flash.gdshader")};
        var flash=new ColorRect {Name="ImpactFlash",Material=_impactMaterial,MouseFilter=Control.MouseFilterEnum.Ignore};
        flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);_root.AddChild(flash);

        _modeLabel = MakeLabel(Locale.Text("external"), 15, Cyan);
        Place(_modeLabel, 0.5f, 0, new Vector2(-130, 22), new Vector2(260, 24));
        _modeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _root.AddChild(_modeLabel);

        SetConnection(ConnectionState.Connecting, "", -1);
    }

    public override void _Process(double delta)
    {
        _pulse += (float)delta;
        _impactStrength*=Mathf.Exp(-(float)delta*8);
        _impactMaterial.SetShaderParameter("strength",_impactStrength);
        if (_overlay.Visible)
            _overlayLabel.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * Mathf.Sin(_pulse * 2.2f));
    }

    public void SetConnection(ConnectionState state, string text, int ping)
    {
        _connectionState = state;
        _connectionText = text;
        _ping = ping;
        _connectionLabel.AddThemeColorOverride("font_color",
            state == ConnectionState.Connected ? Green : state == ConnectionState.Rejected ? Red : Warm);
        _connectionLabel.Text = state switch
        {
            ConnectionState.Connected => $"{ServerAddress}  ·  {ping} ms",
            ConnectionState.Rejected => Locale.Format("link_failed_text", text).ToUpperInvariant(),
            _ => Locale.Text("link_connecting_short"),
        };

        _overlay.Visible = state != ConnectionState.Connected;
        if (state != ConnectionState.Connected)
        {
            _overlayLabel.Text = Locale.Text(state == ConnectionState.Rejected ? "connection_failed" : "establishing");
            _overlayLabel.AddThemeColorOverride("font_color", state == ConnectionState.Rejected ? Red : Cyan);
            var detail = string.IsNullOrWhiteSpace(text) || state != ConnectionState.Rejected ? "" : text + "\n\n";
            _overlayHint.Text = detail + Locale.Text("server_start_hint");
        }
    }

    /// <summary>Re-applies static text after the client language changes.</summary>
    public void RefreshLanguage()
    {
        _modeLabel.Text = Locale.Text(_modeKey);
        RenderSystem();
        SetConnection(_connectionState, _connectionText, _ping);
    }

    public void SetSystem(string systemName)
    {
        _systemName = systemName;
        RenderSystem();
    }

    private void RenderSystem()
    {
        _systemLabel.Text = _systemName;
    }

    public void SetMode(string modeKey, string subtitle)
    {
        _modeKey = modeKey;
        _modeLabel.Text = Locale.Text(modeKey);
        _reticle.Visible = modeKey != "tactical";
        _reticle.ReticleColor = modeKey == "cockpit" ? new Color(1.0f, 0.75f, 0.4f, 0.7f) : new Color(0.55f, 0.85f, 1.0f, 0.72f);
        _reticle.QueueRedraw();
    }

    public void SetTelemetry(float speed, float throttle, float altitude, bool boosting, float warpCooldown, float scanRangeKm, bool autopilot, bool assist, bool following, bool cruising, bool cruiseRequested, bool cruiseBraking)
    {
        _speedLabel.Text = $"{speed,5:F0} m/s";
        _speedLabel.AddThemeColorOverride("font_color", boosting ? Warm : Cyan);
        _throttleBar.Value = (throttle + 1f) * 50f;
        _throttleBar.AddThemeStyleboxOverride("fill", MakeBarFill(boosting ? Warm : Cyan));
        var warp = warpCooldown <= 0 ? Locale.Text("ready") : $"{warpCooldown:F1}s";
        var auto = following ? Locale.Text("following") : autopilot ? Locale.Text("cockpit_autopilot") : Locale.Text("off");
        _telemetryLabel.Text =
            Locale.Format("telemetry_throttle", throttle >= 0 ? "+" : "", throttle * 100, altitude >= 0 ? "+" : "", altitude) + "\n" +
            Locale.Format("telemetry_status", scanRangeKm, warp, auto, assist ? Locale.Text("cockpit_assist") : Locale.Text("off"));
        _warpLabel.Text = cruiseBraking ? Locale.Text("cruise_braking") : cruising ? Locale.Text("cruise_active") : cruiseRequested ? Locale.Text("cruise_pending") : boosting ? Locale.Text("boost_short") : "";
    }

    public void SetScanPending(string targetName) => NoticeRaised?.Invoke(Locale.Text("scanning")+": "+targetName);

    public void ShowScanResult(string title, string body, bool success) => NoticeRaised?.Invoke(title+"\n"+body);

    public void ShowToast(string text, Color? color = null, float duration = 4.0f)
    {
        NoticeRaised?.Invoke(text);
    }

    public void SetQuality(bool high) => ShowToast(Locale.Text(high ? "quality_high" : "quality_low"), Dim, 2.5f);

    private void BuildTopLeft()
    {
        _connectionPanel = CreatePanel();
        Place(_connectionPanel, 0, 0, new Vector2(24, 16), new Vector2(360, 66));
        _root.AddChild(_connectionPanel);
        _connectionPanel.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());

        _connectionLabel = MakeLabel(Locale.Text("link_connecting_short"), 13, Warm);
        Place(_connectionLabel, 0, 0, new Vector2(0, 0), new Vector2(360, 20));
        _connectionPanel.AddChild(_connectionLabel);

        _systemLabel = MakeLabel("VESPER", 18, new Color(.04f,.06f,.08f));
        _systemLabel.AddThemeColorOverride("font_shadow_color",Colors.Transparent);
        var plate = new Panel();
        plate.AddThemeStyleboxOverride("panel",new StyleBoxFlat {BgColor=Colors.White});
        Place(plate,0,0,new Vector2(0,26),new Vector2(360,36));
        _connectionPanel.AddChild(plate);
        Place(_systemLabel, 0, 0, new Vector2(8, 28), new Vector2(344, 32));
        _connectionPanel.AddChild(_systemLabel);
    }

    private void BuildTelemetry()
    {
        var panel = CreatePanel();
        Place(panel, 1, 1, new Vector2(-494, -154), new Vector2(470, 130));
        _root.AddChild(panel);

        _speedLabel = MakeLabel("0 m/s", 34, Cyan);
        Place(_speedLabel, 0, 0, new Vector2(16, 8), new Vector2(220, 46));
        panel.AddChild(_speedLabel);

        _warpLabel = MakeLabel("", 15, Warm);
        Place(_warpLabel, 1, 0, new Vector2(-220, 20), new Vector2(200, 22));
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
        Place(_throttleBar, 0, 0, new Vector2(16, 56), new Vector2(438, 7));
        panel.AddChild(_throttleBar);

        _telemetryLabel = MakeLabel(Locale.Text("game_title"), 13, Dim);
        Place(_telemetryLabel, 0, 0, new Vector2(16, 70), new Vector2(438, 50));
        panel.AddChild(_telemetryLabel);
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

        _overlayLabel = MakeLabel(Locale.Text("establishing"), 34, Cyan);
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
