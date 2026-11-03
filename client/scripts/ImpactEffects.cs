using System;
using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class ImpactEffects : Node3D
{
    private sealed class Burst
    {
        public required ImpactState Hit;
        public float Age;
    }
    private readonly List<Burst> _bursts=new();
    private readonly ImmediateMesh _mesh=new();
    private readonly StandardMaterial3D _material=new()
    {
        ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode=BaseMaterial3D.BlendModeEnum.Add,
        VertexColorUseAsAlbedo=true,
        EmissionEnabled=true,Emission=new Color(1,.5f,.12f),EmissionEnergyMultiplier=1.6f,
    };
    private readonly bool _player;
    private bool _stopped;
    private readonly List<(AudioStreamPlayer3D Audio,Vec3d Point)> _positional=new();
    public int BurstCount=>_bursts.Count;
    public override void _EnterTree()=>ImpactSound.Retain();
    public void StopAudio()
    {
        _stopped=true;
        foreach(var child in GetChildren())
        {
            if(child is AudioStreamPlayer audio){audio.Stop();}
            if(child is AudioStreamPlayer3D spatial){spatial.Stop();}
        }
    }
    public override void _ExitTree()
    {
        StopAudio();
        foreach(var child in GetChildren())
        {
            if(child is MeshInstance3D mesh)mesh.Mesh=null;
        }
        _mesh.Dispose();_material.Dispose();ImpactSound.Release();
    }
    public ImpactEffects(bool player)
    {
        Name="ImpactEffects";_player=player;TopLevel=true;
        AddChild(new MeshInstance3D {Name="Sparks",Mesh=_mesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }
    public void Trigger(ImpactState impact,Vec3d origin,bool particles)
    {
        if(_stopped)return;
        if(particles)
        {
            if(_bursts.Count>=4)_bursts.RemoveAt(0);
            _bursts.Add(new Burst {Hit=impact});
        }
        var level=impact.NormalSpeed>120?3:impact.NormalSpeed>35?2:impact.NormalSpeed>8?1:0;
        if(impact.NormalSpeed>1)Play(ImpactSoundKind.Hit,level,impact.Point,origin);
        if(impact.TangentialSpeed>3)Play(ImpactSoundKind.Scrape,level,impact.Point,origin);
        if(impact.NormalSpeed>15)Play(ImpactSoundKind.Metal,level,impact.Point,origin);
    }
    private void Play(ImpactSoundKind kind,int level,Vec3d point,Vec3d origin)
    {
        var voices=0;
        foreach(var child in GetChildren())if(child is AudioStreamPlayer or AudioStreamPlayer3D)voices++;
        if(voices>=6)return;
        var bus=AudioServer.GetBusIndex("Effects")>=0?"Effects":"Master";
        if(_player)
        {
            var audio=new AudioStreamPlayer {Stream=ImpactSound.Sample(kind,level),Bus=bus,VolumeDb=-13+level*2};
            AddChild(audio);audio.Finished+=audio.QueueFree;audio.Play();
        }
        else
        {
            var audio=new AudioStreamPlayer3D {Stream=ImpactSound.Sample(kind,level),Bus=bus,UnitSize=25,MaxDistance=400,
                Position=ShipTrack.ToVector3(point-origin),VolumeDb=-16+level*2,TopLevel=true};
            AddChild(audio);_positional.Add((audio,point));audio.Finished+=audio.QueueFree;audio.Play();
        }
    }
    public void Step(float delta,Vec3d origin,bool particles)
    {
        GlobalTransform=Transform3D.Identity;_mesh.ClearSurfaces();
        for(var i=_positional.Count-1;i>=0;i--)
        {
            var sound=_positional[i];
            if(!GodotObject.IsInstanceValid(sound.Audio)){_positional.RemoveAt(i);continue;}
            sound.Audio.GlobalPosition=ShipTrack.ToVector3(sound.Point-origin);
        }
        for(var i=_bursts.Count-1;i>=0;i--)
        {
            _bursts[i].Age+=delta;
            if(_bursts[i].Age>.55f||!particles)_bursts.RemoveAt(i);
        }
        if(_bursts.Count==0)return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines,_material);
        foreach(var burst in _bursts)
        {
            var normal=ShipTrack.ToVector3(burst.Hit.Normal);
            var side=normal.Cross(Mathf.Abs(normal.Y)<.9f?Vector3.Up:Vector3.Right).Normalized();
            var up=normal.Cross(side);var age=burst.Age;
            var start=ShipTrack.ToVector3(burst.Hit.Point-origin);
            var energy=Mathf.Clamp(Mathf.Log(1+burst.Hit.NormalSpeed)/4, .3f,1.8f);
            for(var i=0;i<16;i++)
            {
                var phase=i*2.39996f+burst.Hit.Sequence*.7f;
                var direction=(normal*(.08f+i%3*.04f)+side*Mathf.Cos(phase)+up*Mathf.Sin(phase)).Normalized();
                var velocity=direction*(15+i%5*3)*energy;
                var position=start+velocity*(1-Mathf.Exp(-age*2))*.5f;
                var length=(.2f+energy*.35f)*(1-age/.55f);
                _mesh.SurfaceSetColor(new Color(1,.78f,.3f,1-age/.55f));_mesh.SurfaceAddVertex(position);
                _mesh.SurfaceSetColor(new Color(1,.22f,.04f,0));_mesh.SurfaceAddVertex(position-direction*length);
            }
        }
        _mesh.SurfaceEnd();
    }
}
