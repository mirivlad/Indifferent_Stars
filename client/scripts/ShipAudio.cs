using System;
using Godot;

namespace IndifferentStars.Client;

/// <summary>Audio persists while the ship changes representations.</summary>
public sealed partial class ShipAudio : Node
{
    private static readonly string[] MusicPaths =
    {
        "res://assets/music/The Endless Silence.mp3",
        "res://assets/music/A Speck Among Stars.mp3",
    };

    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _engine = null!;
    private AudioStreamPlayer _effects = null!;
    private AudioStreamPlayer _ui = null!;
    private AudioStreamPlayer _radio = null!;
    private AudioStreamGeneratorPlayback? _enginePlayback;
    private int _track;
    private double _phase;
    private double _radioTimer = 48;
    private float _load;
    private bool _previousBrake;

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

    public void SetFlight(float throttle, float maneuver, bool boosting, bool braking)
    {
        _load = Math.Clamp(Math.Abs(throttle) * 0.6f + maneuver * 0.25f + (boosting ? 0.4f : 0), 0, 1);
        if (braking && !_previousBrake) _effects.Play();
        _previousBrake = braking;
    }

    public void NavigationCue() => _ui.Play();

    public override void _Process(double delta)
    {
        _radioTimer -= delta;
        if (_radioTimer <= 0)
        {
            _radio.Play();
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
            var sample = (float)((Math.Sin(_phase) + 0.22 * Math.Sin(_phase * 2.03)) * (0.025 + _load * 0.07));
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

    private void PlayNext()
    {
        var stream = GD.Load<AudioStream>(MusicPaths[_track]);
        _track = (_track + 1) % MusicPaths.Length;
        if (stream is null)
        {
            GD.PushWarning("Ambient music could not be loaded.");
            return;
        }
        _music.Stream = stream;
        _music.Play();
    }

    public override void _ExitTree()
    {
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
}
