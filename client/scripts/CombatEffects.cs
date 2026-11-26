using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public sealed partial class CombatEffects : Node3D
{
    private sealed class Effect
    {
        public required Node3D Node;
        public required Vec3d Position;
        public required float Lifetime;
        public StandardMaterial3D? Material;
        public Vector3 Drift;
        public float Expansion;
        public float Age;
    }

    private readonly List<Effect> _effects = new();
    private readonly Dictionary<int, AudioStreamWav> _audio = new();
    private readonly Dictionary<ulong, (Node3D Node, Vec3d Position)> _wrecks = new();
    private readonly SphereMesh _burst = new() { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 };
    private readonly TorusMesh _ring = new() { InnerRadius = .75f, OuterRadius = 1, Rings = 32, RingSegments = 8 };

    public void Emit(CombatEventMessage message, Vec3d renderOrigin, float intensity, float sound)
    {
        var color = WeaponColor(message.Weapon);
        if (message.Kind == CombatEventKind.MissileTrail)
        {
            if (intensity <= 0) return;
            Mesh(_burst, color, .16f, intensity, message.To, renderOrigin, 3);
            Mesh(_burst, new Color(.56f, .58f, .63f), .52f, intensity * .57f,
                Vec3d.Lerp(message.From, message.To, .55), renderOrigin, 6);
            return;
        }
        if (intensity > 0 && message.Kind == CombatEventKind.Fired)
        {
            var displacement = ShipTrack.ToVector3(message.To - message.From);
            if (displacement.LengthSquared() > .01f)
            {
                var definition = WeaponCatalog.Get(message.Weapon);
                var direction = displacement.Normalized();
                var length = definition.ProjectileSpeed > 0 ? (message.Weapon == WeaponKind.Missile ? 9 : 25) : displacement.Length();
                var beam = new CylinderMesh
                {
                    Height = length, BottomRadius = message.Weapon == WeaponKind.BeamLaser ? 1.1f : .7f,
                    TopRadius = message.Weapon == WeaponKind.Missile ? .12f : .38f, RadialSegments = 6, Rings = 1,
                };
                var start = message.From + new Vec3d(direction.X, direction.Y, direction.Z) * (length / 2);
                var duration = definition.ProjectileSpeed > 0 ? .18f : message.Weapon == WeaponKind.BeamLaser ? .14f : .24f;
                var pulse = Mesh(beam, color, duration, intensity, start, renderOrigin,
                    drift: direction * (float)definition.ProjectileSpeed);
                pulse.Quaternion = new Quaternion(Vector3.Up, direction);
                if (message.Weapon == WeaponKind.Missile)
                    Mesh(_burst, new Color(.61f, .63f, .67f), .62f, intensity * .5f, message.From, renderOrigin, 8);
            }
        }
        else if (intensity > 0 && message.Kind == CombatEventKind.ShieldHit)
        {
            var position = message.To;
            Mesh(_ring, new Color(.12f, .67f, 1), .43f, intensity, position, renderOrigin, 34);
            var vertical = Mesh(_ring, new Color(.42f, .90f, 1), .29f, intensity * .60f, position, renderOrigin, 26);
            vertical.RotateX(Mathf.Pi / 2);
        }
        else if (intensity > 0)
        {
            var destroyed = message.Kind == CombatEventKind.Destroyed;
            var position = message.To;
            Mesh(_burst, new Color(1, .45f, .18f), destroyed ? .63f : .24f, intensity, position, renderOrigin, destroyed ? 46 : 12);
            if (destroyed) Mesh(_ring, new Color(1, .26f, .08f), .82f, intensity, position, renderOrigin, 64);
            Mesh(_burst, new Color(.29f, .32f, .37f), destroyed ? 1.5f : .67f, intensity * .54f,
                position, renderOrigin, destroyed ? 18 : 6, new Vector3(.4f, 2, .15f));
            var random = new Random(unchecked((int)message.Sequence));
            for (var i = 0; i < (destroyed ? 8 : 4); i++)
            {
                var direction = new Vector3(random.NextSingle() - .5f, random.NextSingle() - .5f, random.NextSingle() - .5f).Normalized();
                var spark = Mesh(_burst, new Color(1, .78f, .43f), .29f + .24f * random.NextSingle(),
                    intensity, position, renderOrigin, -.65f, direction * (12 + 28 * random.NextSingle()));
                spark.Scale = new Vector3(.42f, .42f, 1.48f);
                spark.Quaternion = new Quaternion(Vector3.Back, direction);
            }
        }
        if (sound <= 0 || _effects.Count > 78) return;
        var audioPoint = message.Kind == CombatEventKind.Fired ? message.From : message.To;
        var audio = new AudioStreamPlayer3D
        {
            Stream = Sound(message.Kind, message.Weapon), MaxDistance = 2700, UnitSize = 120,
            VolumeDb = Mathf.LinearToDb(sound * .68f),
        };
        AddChild(audio);
        audio.Position = ShipTrack.ToVector3(audioPoint - renderOrigin);
        audio.Play();
        Add(new Effect { Node = audio, Position = audioPoint, Lifetime = (float)audio.Stream.GetLength() + .06f });
    }

    public void Step(float delta, Vec3d renderOrigin)
    {
        foreach (var wreck in _wrecks.Values)
            wreck.Node.Position = ShipTrack.ToVector3(wreck.Position - renderOrigin);
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            effect.Age += delta;
            if (effect.Age >= effect.Lifetime)
            {
                effect.Node.QueueFree();
                _effects.RemoveAt(i);
                continue;
            }
            effect.Node.Position = ShipTrack.ToVector3(effect.Position - renderOrigin) + effect.Drift * effect.Age;
            if (effect.Material is null) continue;
            var color = effect.Material.AlbedoColor;
            color.A *= Mathf.Exp(-delta * 5 / effect.Lifetime);
            effect.Material.AlbedoColor = color;
            if (effect.Expansion != 0)
                effect.Node.Scale = Vector3.One * Mathf.Max(.12f, 1 + effect.Age * effect.Expansion);
        }
    }

    public void StopAudio()
    {
        foreach (var effect in _effects)
            if (effect.Node is AudioStreamPlayer3D player) player.Stop();
    }

    public static Color WeaponColor(WeaponKind kind) => kind switch
    {
        WeaponKind.PulseLaser => new Color(1, .36f, .27f),
        WeaponKind.BeamLaser => new Color(.40f, .85f, 1),
        WeaponKind.Cannon => new Color(1, .68f, .31f),
        WeaponKind.Missile => new Color(1, .56f, .22f),
        _ => new Color(.84f, .91f, .37f),
    };

    public void EngineLeak(Vec3d point, Vec3d origin, float intensity)
    {
        if (intensity <= 0) return;
        Mesh(_burst, new Color(.35f, .36f, .43f), .82f, intensity, point, origin, 7, new Vector3(.8f, 3.4f, 1.2f));
        Mesh(_burst, new Color(.38f, .70f, 1), .21f, intensity, point, origin, 4);
    }

    public void SetWrecks(WreckContact[] contacts)
    {
        foreach (var id in _wrecks.Keys.Where(id => !Array.Exists(contacts, item => item.Id == id)).ToArray())
        {
            _wrecks[id].Node.QueueFree();
            _wrecks.Remove(id);
        }
        foreach (var contact in contacts)
        {
            if (_wrecks.ContainsKey(contact.Id)) continue;
            var node = new Node3D();
            var random = new Random(unchecked((int)contact.Id));
            for (var i = 0; i < 7; i++)
            {
                var offset = new Vector3(random.NextSingle() - .5f, random.NextSingle() - .5f, random.NextSingle() - .5f);
                var size = 1.3f + 2.9f * random.NextSingle();
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new PrismMesh { Size = new Vector3(size, size * .3f, size * 1.8f) },
                    Position = offset * 24,
                    Rotation = offset * Mathf.Tau,
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(.20f, .16f, .14f).Lerp(new Color(.53f, .44f, .33f), random.NextSingle()),
                        Metallic = .72f, Roughness = .85f,
                    },
                });
            }
            AddChild(node);
            _wrecks.Add(contact.Id, (node, contact.Position));
        }
    }

    private MeshInstance3D Mesh(Mesh mesh, Color color, float life, float intensity, Vec3d position, Vec3d origin,
        float expansion = 0, Vector3 drift = default)
    {
        color.A = Mathf.Clamp(intensity, 0, 1);
        var material = new StandardMaterial3D
        {
            AlbedoColor = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material,
            Position = ShipTrack.ToVector3(position - origin), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(node);
        Add(new Effect { Node = node, Material = material, Lifetime = life, Position = position, Drift = drift, Expansion = expansion });
        return node;
    }

    private void Add(Effect effect)
    {
        if (_effects.Count == 96)
        {
            _effects[0].Node.QueueFree();
            _effects.RemoveAt(0);
        }
        _effects.Add(effect);
    }

    private AudioStreamWav Sound(CombatEventKind kind, WeaponKind weapon)
    {
        var key = (int)kind * 16 + (int)weapon;
        if (_audio.TryGetValue(key, out var cached)) return cached;
        const int rate = 22050;
        var seconds = kind == CombatEventKind.Destroyed ? .82 : kind == CombatEventKind.Fired ? .25 : .31;
        var laser = kind is CombatEventKind.Fired && weapon is WeaponKind.PulseLaser or WeaponKind.BeamLaser;
        var frequency = laser ? 770 : kind == CombatEventKind.ShieldHit ? 356 : kind == CombatEventKind.Destroyed ? 57 : 104;
        var samples = new byte[(int)(seconds * rate) * 2];
        var random = new Random(key * 519);
        for (var i = 0; i < samples.Length / 2; i++)
        {
            var t = (double)i / rate;
            var noise = (random.NextDouble() * 2 - 1) * Math.Exp(-t * (laser ? 35 : 16));
            var sweep = Math.Sin(Math.Tau * frequency * (t - .63 * t * t));
            var envelope = (1 - Math.Exp(-t * 520)) * Math.Max(0, 1 - t / seconds) * Math.Exp(-t * 4);
            var value = (short)((noise * (laser ? .10 : .39) + sweep * .36) * envelope * short.MaxValue);
            samples[2 * i] = (byte)value;
            samples[2 * i + 1] = (byte)(value >> 8);
        }
        var stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = samples };
        _audio.Add(key, stream);
        return stream;
    }
}
