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
    private int _track;

    public override void _Ready()
    {
        if (AudioServer.GetBusIndex("Music") < 0)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, "Music");
            AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
        }
        _music = new AudioStreamPlayer { Bus = "Music", VolumeDb = -12 };
        AddChild(_music);
        _music.Finished += PlayNext;
        PlayNext();
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
        _music.Stop();
        var stream = _music.Stream;
        _music.Stream = null;
        stream?.Dispose();
    }
}
