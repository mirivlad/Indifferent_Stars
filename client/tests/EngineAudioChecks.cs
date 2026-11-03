using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IndifferentStars.Client;

public sealed partial class EngineAudioChecks : Node
{
    public override async void _Ready()
    {
        ShipAudio? audio = null;
        try
        {
            var output = System.Environment.GetEnvironmentVariable("IS_ENGINE_CAPTURE") ?? "/tmp/indifferent-engines";
            Directory.CreateDirectory(output);
            var voice = new EngineVoice();
            var bytes = new List<byte>();
            var rms = new Dictionary<string,double>();
            double peak=0, jump=0, previous=0;
            foreach(var (name,seconds,throttle,maneuver,boost,brake,cruise,active) in new[]
            {
                ("off",1,0f,0f,false,false,false,false),
                ("idle",2,0f,0f,false,false,false,true),
                ("thrust",4,1f,0f,false,false,false,true),
                ("boost",4,1f,0f,true,false,false,true),
                ("cruise",4,1f,0f,false,false,true,true),
                ("reverse",3,-.65f,0f,false,false,false,true),
                ("maneuver",2,0f,1f,false,false,false,true),
                ("braking",2,0f,0f,false,true,false,true),
                ("release",4,0f,0f,false,false,false,true),
                ("docked",2,0f,0f,false,false,false,false),
            })
            {
                voice.SetControls(throttle,maneuver,boost,brake,cruise,active);
                double power=0,dc=0,stereo=0;var measured=0;
                for(var i=0;i<seconds*EngineVoice.SampleRate;i++)
                {
                    var (left,right)=voice.NextFrame();
                    Check(float.IsFinite(left)&&float.IsFinite(right),"Samples must remain finite.");
                    peak=Math.Max(peak,Math.Max(Math.Abs(left),Math.Abs(right)));
                    jump=Math.Max(jump,Math.Abs(left-previous));previous=left;
                    if(i>=(seconds-1)*EngineVoice.SampleRate)
                    {power+=(left*left+right*right)*.5;dc+=(left+right)*.5;stereo+=Math.Abs(left-right);measured++;}
                    foreach(var sample in new[]{left,right})
                    {var pcm=(short)Math.Round(Math.Clamp(sample,-1,1)*32767);bytes.Add((byte)pcm);bytes.Add((byte)(pcm>>8));}
                }
                rms[name]=Math.Sqrt(power/measured);
                Check(Math.Abs(dc/measured)<.01,"No sustained DC offset.");
                if(name=="maneuver")Check(stereo/measured>.001,"Maneuver layer must retain stereo width.");
                GD.Print($"ENGINE {name}: RMS={rms[name]:F6}");
            }
            Check(peak<.85,"Engine must retain headroom for warp and other buses.");
            Check(jump<.3,"No large sample discontinuities across control changes.");
            Check(rms["off"]==0&&rms["docked"]<.00001,"Menu and docked engine must become silent.");
            Check(rms["thrust"]>rms["idle"]*3&&rms["boost"]>rms["thrust"]*1.2,"Thrust and boost must be audibly separated.");
            Check(rms["release"]<rms["thrust"]*.2,"Engine must spool back down after release.");
            using(var stream=File.Create(Path.Combine(output,"engine_sequence.wav")))
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write("RIFF"u8);writer.Write(36+bytes.Count);writer.Write("WAVEfmt "u8);writer.Write(16);
                writer.Write((short)1);writer.Write((short)2);writer.Write(EngineVoice.SampleRate);
                writer.Write(EngineVoice.SampleRate*4);writer.Write((short)4);writer.Write((short)16);
                writer.Write("data"u8);writer.Write(bytes.Count);writer.Write(bytes.ToArray());
            }
            // Exercise the actual Godot streaming player and lifecycle as well as the exact synthesis.
            audio=new ShipAudio();AddChild(audio);
            AudioServer.SetBusMute(AudioServer.GetBusIndex("Music"),true);
            var engine=audio.GetNode<AudioStreamPlayer>("EngineSound");
            var playback=(AudioStreamGeneratorPlayback)engine.GetStreamPlayback();
            await Pause(.2);
            var skips=playback.GetSkips();
            foreach(var boost in new[]{false,true,false})
            {audio.SetFlight(1,.3f,boost,false);await Pause(.5);Check(engine.Playing,"Engine stream must keep playing across control changes.");}
            var finalSkips=playback.GetSkips();
            GD.Print($"ENGINE STREAM: start skips={skips}, end skips={finalSkips}");
            Check(finalSkips==skips,"Streaming must keep the engine buffer supplied.");
            audio.SetFlight(0,0,false,false,active:false);await Pause(.3);
            audio.Stop();audio.QueueFree();await Pause(.15);
            if(System.Environment.GetEnvironmentVariable("IS_ENGINE_FLIGHT")=="1")await CheckFlight();
            GD.Print($"ENGINE AUDIO CHECKS PASSED: 28s stereo preview, peak={peak:F4}, max jump={jump:F4}, no clipping/DC, spool/release, stream and shutdown.");
            GetTree().Quit();
        }
        catch(Exception error){GD.PrintErr(error);audio?.Stop();await Pause(.15);GetTree().Quit(1);}
    }
    private async Task CheckFlight()
    {
        var game=GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<GameMain>();AddChild(game);
        var bus=AudioServer.GetBusIndex("Engines");
        var capture=new AudioEffectCapture {BufferLength=.5f};AudioServer.AddBusEffect(bus,capture);
        try
        {
            await Pause(.3);
            Check(await Level()<.00001,"Engine must be silent in the main menu.");
            var menu=game.FindChildren("*","CanvasLayer",true,false).OfType<SettingsMenu>().Single();
            menu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==Locale.Text("play")).EmitSignal(Button.SignalName.Pressed);
            ShipView? pilot=null;var end=Time.GetTicksMsec()+12000;
            while(pilot is null&&Time.GetTicksMsec()<end)
            {pilot=game.FindChildren("Ship_*","Node3D",true,false).OfType<ShipView>().FirstOrDefault(s=>s.IsPlayer&&s.Track.HasData);await Pause(.05);}
            Check(pilot is not null,"Real server connection required.");
            SetKey(Key.F2,true);SetKey(Key.F2,false);await Pause(.5);
            var idle=await Level();
            SetKey(Key.S,true);await Pause(1.5);var thrust=await Level();
            Check(Math.Abs(pilot!.Track.Throttle)>.8&&thrust>idle*3,"Authoritative reverse thrust must drive the engine bus.");
            SetKey(Key.Shift,true);await Pause(1);var boost=await Level();
            Check(boost>thrust*1.2,"Boost input must change the live engine sound.");
            SetKey(Key.S,false);SetKey(Key.Shift,false);SetKey(Key.Space,true);await Pause(1);SetKey(Key.Space,false);
            var audio=game.GetNode<ShipAudio>("ShipAudio");
            foreach(var mode in new[]{Key.F1,Key.F2,Key.F3})
            {SetKey(mode,true);SetKey(mode,false);await Pause(.2);Check(ReferenceEquals(audio,game.GetNode<ShipAudio>("ShipAudio")),"View changes must preserve the audio node.");}
            GD.Print($"ENGINE FLIGHT PASSED: engine bus RMS idle={idle:F5}, reverse={thrust:F5}, boost={boost:F5}; menu silence, three views and graceful stop.");
        }
        finally
        {
            SetKey(Key.S,false);SetKey(Key.Shift,false);SetKey(Key.Space,false);
            AudioServer.RemoveBusEffect(bus,0);capture.Dispose();
            game.GetNode<ShipAudio>("ShipAudio").Stop();await Pause(.25);game.QueueFree();await Pause(.05);
        }
        async Task<double> Level()
        {
            capture.ClearBuffer();await Pause(.25);var frames=capture.GetBuffer(capture.GetFramesAvailable());
            Check(frames.Length>0,"Engine bus capture must receive actual audio frames.");
            return Math.Sqrt(frames.Sum(frame=>(double)frame.LengthSquared())/(2*frames.Length));
        }
    }
    private static void SetKey(Key key,bool pressed)=>Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed});
    private async Task Pause(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
