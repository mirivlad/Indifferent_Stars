using System;
using System.IO;
using System.Linq;
using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

/// <summary>Audio persists while the ship changes representations.</summary>
public sealed partial class ShipAudio : Node
{
    public static string[] GetBundledMusicPaths() => ResourceLoader.ListDirectory("res://assets/music")
        .Where(name => name.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase))
        .OrderBy(name => name,StringComparer.Ordinal)
        .Select(name => "res://assets/music/"+name).ToArray();
    private string[] _bundledTracks = Array.Empty<string>();

    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _engine = null!;
    private AudioStreamPlayer _effects = null!;
    private AudioStreamPlayer _ui = null!;
    private AudioStreamPlayer _radio = null!;
    private AudioStreamGeneratorPlayback? _enginePlayback;
    private string[] _customTracks = Array.Empty<string>();
    private string _musicDirectory = "";
    private int _track;
    private bool _previousThrust;
    private float _maneuver;
    private bool _boosting;
    private double _phase;
    private double _radioTimer = 48;
    private float _load;
    private float _warpEnergy;
    private double _warpPhase;
    private bool _previousBrake;
    private bool _stopped;

    public override void _Ready()
    {
        foreach (var bus in new[] { "Music", "Engines", "Effects", "UI", "Radio" })
        {
            if (AudioServer.GetBusIndex(bus) >= 0) continue;
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, bus);
            AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
        }
        _music = new AudioStreamPlayer { Name = "AmbientMusic", Bus = "Music", VolumeDb = -12 };
        AddChild(_music);
        _bundledTracks = GetBundledMusicPaths();
        _music.Finished += PlayNext;
        PlayNext();
        _engine = new AudioStreamPlayer
        {
            Name = "EngineSound",
            Bus = "Engines",
            Stream = new AudioStreamGenerator { MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom, MixRate = 22050, BufferLength = 0.15f },
        };
        AddChild(_engine);
        _engine.Play();
        _enginePlayback = _engine.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _effects = MakeCuePlayer("Effects", 160);
        _ui = MakeCuePlayer("UI", 540);
        _radio = MakeCuePlayer("Radio", 760);
    }

    public void SetWarp(WarpStage stage, float progress)
    {
        _warpEnergy = stage switch
        {
            WarpStage.Charging => progress,
            WarpStage.Travel => 1,
            WarpStage.Exit or WarpStage.Recovery => 1 - progress,
            _ => 0,
        };
    }

    public void SetFlight(float throttle, float maneuver, bool boosting, bool braking)
    {
        if (_stopped) return;
        var thrust = Math.Abs(throttle) > 0.05;
        if (thrust != _previousThrust)
        {
            _effects.PitchScale = thrust ? 1.5f : 0.65f;
            _effects.Play();
        }
        _previousThrust = thrust;
        _maneuver = maneuver;
        _boosting = boosting;
        _load = Math.Clamp(Math.Abs(throttle) * 0.6f + maneuver * 0.25f + (boosting ? 0.4f : 0), 0, 1);
        if (braking && !_previousBrake) { _effects.PitchScale = 1; _effects.Play(); }
        _previousBrake = braking;
    }

    public void RadioCue()
    {
        if (_stopped) return;
        var previous = _radio.Stream;
        _radio.Stream = RadioBurst();
        previous?.Dispose();
        _radio.Play();
    }

    public void NavigationCue() { if (!_stopped) _ui.Play(); }

    public override void _Process(double delta)
    {
        if (_stopped) return;
        _radioTimer -= delta;
        if (_radioTimer <= 0)
        {
            RadioCue();
            _radioTimer = 48 + GD.RandRange(0, 35);
        }
        if (_enginePlayback is null) return;
        var count = Math.Min(_enginePlayback.GetFramesAvailable(), 4096);
        if (count <= 0) return;
        var frames = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            _phase += Math.Tau * (48 + _load * 38) / 22050;
            if (_phase > Math.Tau) _phase -= Math.Tau;
            var sample = (float)((Math.Sin(_phase) + 0.22 * Math.Sin(_phase * 2.03)) * (0.025 + _load * 0.07)
                + Math.Sin(_phase * 4.7) * _maneuver * 0.012
                + (_boosting ? Math.Sin(_phase * 0.51) * 0.025 : 0));
            _warpPhase += Math.Tau * (80 + _warpEnergy * 220) / 22050;
            if (_warpPhase > Math.Tau) _warpPhase -= Math.Tau;
            sample += (float)(Math.Sin(_warpPhase) * Math.Sin(_warpPhase * 0.503) * _warpEnergy * 0.06);
            frames[i] = new Vector2(sample, sample);
        }
        _enginePlayback.PushBuffer(frames);
    }

    private AudioStreamPlayer MakeCuePlayer(string bus, double frequency)
    {
        const int sampleRate = 22050;
        var count = (int)(sampleRate * 0.22);
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var envelope = Math.Sin(Math.PI * i / count);
            var sample = (short)(Math.Sin(Math.Tau * frequency * i / sampleRate) * envelope * 3500);
            bytes[i * 2] = (byte)sample;
            bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        var player = new AudioStreamPlayer
        {
            Name = bus + "Cue",
            Bus = bus,
            Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sampleRate, Stereo = false, Data = bytes },
        };
        AddChild(player);
        return player;
    }

    public void SetMusicDirectory(string directory)
    {
        directory = directory.Trim();
        if (_musicDirectory == directory) return;
        _musicDirectory = directory;
        _customTracks = Array.Empty<string>();
        _track = 0;
        if (!Directory.Exists(directory)) return;
        try
        {
            _customTracks = Directory.EnumerateFiles(directory)
                .Where(path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        catch (IOException) { GD.PushWarning("Could not read music directory."); }
        catch (UnauthorizedAccessException) { GD.PushWarning("Music directory is not readable."); }
        // Finish the current track; changing views or settings never restarts music.
    }

    private static AudioStreamWav RadioBurst()
    {
        const int rate = 22050;
        var random = new Random();
        var mode = random.Next(3);
        var length = rate * (mode + 1) / 3;
        var bytes = new byte[length * 2];
        for (var i = 0; i < length; i++)
        {
            var t = (double)i / rate;
            var envelope = Math.Sin(Math.PI * i / length);
            var noise = (random.NextDouble() * 2 - 1) * (mode == 0 ? 0.14 : 0.035);
            var tone = mode == 0 ? 0 : Math.Sin(Math.Tau * (mode == 1 ? 680 : 940) * t) *
                (Math.Sin(t * 35) > 0 ? 0.1 : 0.02);
            var sample = (short)((noise + tone) * envelope * 12000);
            bytes[i * 2] = (byte)sample;
            bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = bytes };
    }

    private void PlayNext()
    {
        if (_stopped) return;
        AudioStream? stream = null;
        var paths = _customTracks.Length > 0 ? _customTracks : _bundledTracks;
        for (var attempt = 0; attempt < paths.Length && stream is null; attempt++)
        {
            var path = paths[_track % paths.Length];
            _track = (_track + 1) % paths.Length;
            stream = _customTracks.Length > 0 ? AudioStreamMP3.LoadFromFile(path) : GD.Load<AudioStream>(path);
        }
        if (stream is null)
        {
            GD.PushWarning("Ambient music could not be loaded.");
            return;
        }
        _music.Stream = stream;
        _music.Play();
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _music.Finished -= PlayNext;
        _enginePlayback?.Dispose();
        foreach (var player in new[] { _music, _engine, _effects, _ui, _radio })
        {
            player.Stop();
            var stream = player.Stream;
            player.Stream = null;
            stream?.Dispose();
        }
    }

    public override void _ExitTree() => Stop();
}
