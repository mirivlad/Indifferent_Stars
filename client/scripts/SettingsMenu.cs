using System;
using System.Linq;
using Godot;

namespace IndifferentStars.Client;

public sealed partial class SettingsMenu : CanvasLayer
{
    private readonly ClientSettings _settings;
    private readonly Action _apply;
    private readonly Action _start;
    private readonly Action _quit;
    private Control? _root;
    private string? _binding;
    private bool _started;

    public SettingsMenu(ClientSettings settings, Action apply, Action start, Action quit)
    {
        _settings = settings;
        _apply = apply;
        _start = start;
        _quit = quit;
        Layer = 30;
    }

    public bool IsOpen => Visible;

    public override void _Ready() => ShowMenu(false);

    public void ShowMenu(bool started)
    {
        _started = started;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        BuildMenu();
    }

    private void Clear()
    {
        if (_root is not null) { RemoveChild(_root); _root.QueueFree(); }
        _root = new Control();
        _root.Theme = new Theme { DefaultFontSize = 22 };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var shade = new ColorRect { Color = new Color(0.005f, 0.012f, 0.025f, 0.88f) };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(shade);
    }

    private void BuildMenu()
    {
        Clear();
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(540, 320) };
        box.SetAnchorsPreset(Control.LayoutPreset.Center);
        box.Position = new Vector2(-270, -160);
        _root!.AddChild(box);
        box.AddChild(new Label { Text = "INDIFFERENT STARS", HorizontalAlignment = HorizontalAlignment.Center });
        var start = AddButton(box, _started ? "resume" : "play", () =>
        {
            Visible = false;
            if (!_started) { _started = true; _start(); }
        });
        AddButton(box, "settings", BuildSettings);
        AddButton(box, "quit", _quit);
        start.GrabFocus();
    }

    private void BuildSettings()
    {
        Clear();
        var box = new VBoxContainer();
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 35);
        _root!.AddChild(box);
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(tabs);
        var graphics = Tab(tabs, "graphics");
        AddChoices(graphics, "preset", new[] { Locale.Text("custom"), Locale.Text("low"), Locale.Text("medium"), Locale.Text("high"), Locale.Text("ultra") }, 0, index =>
        {
            if (index == 0) return;
            _settings.RenderScale = index switch { 1 => 0.6f, 2 => 0.8f, 3 => 0.9f, _ => 1f };
            _settings.Glow = index >= 2;
            _settings.Antialiasing = index >= 3;
            _settings.Shadows = index >= 4;
            _settings.EngineEffects = index >= 2;
            _settings.Particles = index >= 2;
            _settings.SkyDetail = Math.Min(index - 1, 2);
            Callable.From(BuildSettings).CallDeferred();
        });
        var resolutions = new[] { "auto", "1280x720", "1600x900", "1920x1080" };
        AddChoices(graphics, "resolution", new[] { Locale.Text("auto"), "1280 × 720", "1600 × 900", "1920 × 1080" },
            Math.Max(0, Array.IndexOf(resolutions, _settings.Resolution)), index => _settings.Resolution = resolutions[index]);
        AddToggle(graphics, "fullscreen", _settings.Fullscreen, value => _settings.Fullscreen = value);
        AddSlider(graphics, "render_scale", 0.5, 1, 0.05, _settings.RenderScale, value => _settings.RenderScale = (float)value);
        AddToggle(graphics, "glow", _settings.Glow, value => _settings.Glow = value);
        AddToggle(graphics, "antialiasing", _settings.Antialiasing, value => _settings.Antialiasing = value);
        AddToggle(graphics, "shadows", _settings.Shadows, value => _settings.Shadows = value);
        AddToggle(graphics, "engine_effects", _settings.EngineEffects, value => _settings.EngineEffects = value);
        AddToggle(graphics, "particles", _settings.Particles, value => _settings.Particles = value);
        AddChoices(graphics, "sky_detail", new[] { Locale.Text("low"), Locale.Text("medium"), Locale.Text("high") }, _settings.SkyDetail, index => _settings.SkyDetail = index);
        var sound = Tab(tabs, "sound");
        AddSlider(sound, "master", 0, 1, 0.05, _settings.MasterVolume, value => _settings.MasterVolume = (float)value);
        AddSlider(sound, "music", 0, 1, 0.05, _settings.MusicVolume, value => _settings.MusicVolume = (float)value);
        AddSlider(sound, "engines", 0, 1, 0.05, _settings.EngineVolume, value => _settings.EngineVolume = (float)value);
        AddSlider(sound, "effects", 0, 1, 0.05, _settings.EffectsVolume, value => _settings.EffectsVolume = (float)value);
        AddSlider(sound, "ui_audio", 0, 1, 0.05, _settings.UiVolume, value => _settings.UiVolume = (float)value);
        AddSlider(sound, "radio", 0, 1, 0.05, _settings.RadioVolume, value => _settings.RadioVolume = (float)value);
        var controls = Tab(tabs, "controls");
        AddSlider(controls, "sensitivity", 0.001, 0.1, 0.001, _settings.MouseSensitivity, value => _settings.MouseSensitivity = (float)value);
        AddToggle(controls, "invert_x", _settings.InvertX, value => _settings.InvertX = value);
        AddToggle(controls, "invert_y", _settings.InvertY, value => _settings.InvertY = value);
        AddToggle(controls, "auto_capture", _settings.AutoCapture, value => _settings.AutoCapture = value);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 240), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        controls.AddChild(scroll);
        var keys = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(keys);
        foreach (var (action, key) in _settings.Keys)
        {
            var row = Row(keys, action);
            var button = new Button { Text = OS.GetKeycodeString(key), CustomMinimumSize = new Vector2(220, 0) };
            button.Pressed += () => { _binding = action; button.Text = Locale.Text("press_key"); };
            row.AddChild(button);
        }
        var language = Tab(tabs, "language");
        foreach (var (code, label) in new[] { ("ru", "Русский"), ("en", "English") })
        {
            var button = new Button { Text = label, CustomMinimumSize = new Vector2(0, 48), Disabled = _settings.Language == code };
            button.Pressed += () =>
            {
                _settings.Language = code;
                Locale.Load(_settings.Language);
                Changed();
                Callable.From(BuildSettings).CallDeferred();
            };
            language.AddChild(button);
        }
        var network = Tab(tabs, "network");
        AddEntry(network, "server", _settings.Server, value => _settings.Server = value);
        AddEntry(network, "callsign", _settings.Callsign, value => _settings.Callsign = value);
        AddButton(box, "back", BuildMenu);
    }

    public override void _Input(InputEvent input)
    {
        if (!Visible || input is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (_binding is null)
        {
            if (key.Keycode == _settings.Keys["menu"] && _started)
            {
                Visible = false;
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        GetViewport().SetInputAsHandled();
        if (key.Keycode != Key.Escape && !_settings.Keys.Any(pair => pair.Key != _binding && pair.Value == key.Keycode))
        {
            _settings.Keys[_binding] = key.Keycode;
            Changed();
        }
        _binding = null;
        Callable.From(BuildSettings).CallDeferred();
    }

    private VBoxContainer Tab(TabContainer parent, string title)
    {
        var tab = new VBoxContainer { Name = Locale.Text(title) };
        parent.AddChild(tab);
        return tab;
    }

    private static HBoxContainer Row(VBoxContainer parent, string label)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = Locale.Text(label), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        parent.AddChild(row);
        return row;
    }

    private void AddToggle(VBoxContainer parent, string label, bool initial, Action<bool> set)
    {
        var row = Row(parent, label);
        var control = new CheckButton { ButtonPressed = initial };
        control.Toggled += value => { set(value); Changed(); };
        row.AddChild(control);
    }

    private void AddSlider(VBoxContainer parent, string label, double min, double max, double step, double initial, Action<double> set)
    {
        var row = Row(parent, label);
        var control = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = initial, CustomMinimumSize = new Vector2(260, 0) };
        control.ValueChanged += value => { set(value); Changed(); };
        row.AddChild(control);
    }

    private void AddEntry(VBoxContainer parent, string label, string initial, Action<string> set)
    {
        var row = Row(parent, label);
        var entry = new LineEdit { Text = initial, CustomMinimumSize = new Vector2(300, 0) };
        entry.TextChanged += value => { set(value); Changed(); };
        row.AddChild(entry);
    }

    private void AddChoices(VBoxContainer parent, string label, string[] values, int initial, Action<int> set)
    {
        var row = Row(parent, label);
        var option = new OptionButton { CustomMinimumSize = new Vector2(260, 0) };
        foreach (var value in values) option.AddItem(value);
        option.Select(initial);
        option.ItemSelected += value => { set((int)value); Changed(); };
        row.AddChild(option);
    }

    private static Button AddButton(VBoxContainer parent, string label, Action action)
    {
        var button = new Button { Text = Locale.Text(label), CustomMinimumSize = new Vector2(0, 48) };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void Changed()
    {
        if (_settings.Save() != Error.Ok) GD.PushWarning("Could not save client settings.");
        _apply();
    }
}
