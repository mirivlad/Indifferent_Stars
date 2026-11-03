using System;
using System.Collections.Generic;
using Godot;

namespace IndifferentStars.Client;

public sealed class ClientSettings
{
    private const string Path = "user://settings.cfg";
    public bool DefaultFlightAssist { get; set; }
    public string MusicDirectory { get; set; } = "";
    public string Language { get; set; } = "ru";
    public string Server { get; set; } = "127.0.0.1:7777";
    public string Callsign { get; set; } = "Captain";
    public float MouseSensitivity { get; set; } = 0.025f;
    public float CameraSensitivity { get; set; } = 0.005f;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    public bool AutoCapture { get; set; }
    public bool Fullscreen { get; set; }
    public string Resolution { get; set; } = "auto";
    public float RenderScale { get; set; } = 1;
    public bool Glow { get; set; } = true;
    public bool Antialiasing { get; set; } = true;
    public bool Shadows { get; set; }
    public bool EngineEffects { get; set; } = true;
    public bool Particles { get; set; } = true;
    public int SkyDetail { get; set; } = 2;
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.5f;
    public float EngineVolume { get; set; } = 0.7f;
    public float EffectsVolume { get; set; } = 0.7f;
    public float UiVolume { get; set; } = 0.7f;
    public float RadioVolume { get; set; } = 0.4f;

    public Dictionary<string, Key> Keys { get; } = new()
    {
        ["camera_look"] = Key.Alt, ["forward"] = Key.W, ["reverse"] = Key.S, ["left"] = Key.A, ["right"] = Key.D,
        ["up"] = Key.R, ["down"] = Key.C, ["roll_left"] = Key.Q, ["roll_right"] = Key.E,
        ["yaw_left"] = Key.Left, ["yaw_right"] = Key.Right, ["pitch_up"] = Key.Up, ["pitch_down"] = Key.Down,
        ["cruise"] = Key.V, ["boost"] = Key.Shift, ["brake"] = Key.Space, ["assist"] = Key.Z,
        ["view_2d"] = Key.F1, ["view_3d"] = Key.F2, ["view_cockpit"] = Key.F3,
        ["quality"] = Key.F4, ["cursor"] = Key.Tab, ["scan"] = Key.F, ["warp"] = Key.X,
        ["approach"] = Key.G, ["dock"] = Key.H, ["map_system"] = Key.M, ["map_ship"] = Key.Home,
        ["menu"] = Key.Escape,
    };

    public static ClientSettings Load()
    {
        var settings = new ClientSettings();
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok) return settings;
        settings.Language = file.GetValue("general", "language", settings.Language).AsString();
        if (settings.Language is not ("ru" or "en")) settings.Language = "ru";
        settings.DefaultFlightAssist = file.GetValue("input", "default_assist", false).AsBool();
        settings.MusicDirectory = file.GetValue("audio", "music_directory", "").AsString();
        settings.Server = file.GetValue("network", "server", settings.Server).AsString();
        settings.Callsign = file.GetValue("network", "callsign", settings.Callsign).AsString();
        settings.MouseSensitivity = Math.Clamp(file.GetValue("input", "sensitivity", settings.MouseSensitivity).AsSingle(), 0.001f, 0.1f);
        settings.CameraSensitivity = Math.Clamp(file.GetValue("input", "camera_sensitivity", 0.005f).AsSingle(), 0.001f, 0.03f);
        settings.InvertX = file.GetValue("input", "invert_x", false).AsBool();
        settings.InvertY = file.GetValue("input", "invert_y", false).AsBool();
        settings.AutoCapture = file.GetValue("input", "auto_capture", false).AsBool();
        settings.Fullscreen = file.GetValue("graphics", "fullscreen", false).AsBool();
        settings.Resolution = file.GetValue("graphics", "resolution", "auto").AsString();
        settings.RenderScale = Math.Clamp(file.GetValue("graphics", "scale", 1f).AsSingle(), 0.5f, 1f);
        settings.Glow = file.GetValue("graphics", "glow", true).AsBool();
        settings.Antialiasing = file.GetValue("graphics", "antialiasing", true).AsBool();
        settings.Shadows = file.GetValue("graphics", "shadows", false).AsBool();
        settings.EngineEffects = file.GetValue("graphics", "engine_effects", true).AsBool();
        settings.Particles = file.GetValue("graphics", "particles", true).AsBool();
        settings.SkyDetail = Math.Clamp(file.GetValue("graphics", "sky_detail", 2).AsInt32(), 0, 2);
        settings.MasterVolume = Math.Clamp(file.GetValue("audio", "master", 0.8f).AsSingle(), 0, 1);
        settings.MusicVolume = Math.Clamp(file.GetValue("audio", "music", 0.5f).AsSingle(), 0, 1);
        settings.EngineVolume = Math.Clamp(file.GetValue("audio", "engines", 0.7f).AsSingle(), 0, 1);
        settings.EffectsVolume = Math.Clamp(file.GetValue("audio", "effects", 0.7f).AsSingle(), 0, 1);
        settings.UiVolume = Math.Clamp(file.GetValue("audio", "ui", 0.7f).AsSingle(), 0, 1);
        settings.RadioVolume = Math.Clamp(file.GetValue("audio", "radio", 0.4f).AsSingle(), 0, 1);
        foreach (var action in new List<string>(settings.Keys.Keys))
            settings.Keys[action] = (Key)file.GetValue("keys", action, (long)settings.Keys[action]).AsInt64();
        return settings;
    }

    public Error Save()
    {
        var file = new ConfigFile();
        file.SetValue("input", "default_assist", DefaultFlightAssist);
        file.SetValue("audio", "music_directory", MusicDirectory);
        file.SetValue("general", "language", Language);
        file.SetValue("network", "server", Server);
        file.SetValue("network", "callsign", Callsign);
        file.SetValue("input", "sensitivity", MouseSensitivity);
        file.SetValue("input", "camera_sensitivity", CameraSensitivity);
        file.SetValue("input", "invert_x", InvertX);
        file.SetValue("input", "invert_y", InvertY);
        file.SetValue("input", "auto_capture", AutoCapture);
        file.SetValue("graphics", "fullscreen", Fullscreen);
        file.SetValue("graphics", "resolution", Resolution);
        file.SetValue("graphics", "scale", RenderScale);
        file.SetValue("graphics", "glow", Glow);
        file.SetValue("graphics", "antialiasing", Antialiasing);
        file.SetValue("graphics", "shadows", Shadows);
        file.SetValue("graphics", "engine_effects", EngineEffects);
        file.SetValue("graphics", "particles", Particles);
        file.SetValue("graphics", "sky_detail", SkyDetail);
        file.SetValue("audio", "master", MasterVolume);
        file.SetValue("audio", "music", MusicVolume);
        file.SetValue("audio", "engines", EngineVolume);
        file.SetValue("audio", "effects", EffectsVolume);
        file.SetValue("audio", "ui", UiVolume);
        file.SetValue("audio", "radio", RadioVolume);
        foreach (var (action, key) in Keys) file.SetValue("keys", action, (long)key);
        return file.Save(Path);
    }

    public bool Pressed(string action) => Input.IsKeyPressed(Keys[action]);
}
