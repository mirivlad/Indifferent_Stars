using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

/// <summary>
/// Visual ship entity. Position and orientation always come from a SnapshotTrack;
/// this class never integrates movement itself.
/// </summary>
public sealed partial class ShipView : Node3D
{
    private readonly List<MeshInstance3D> _engineGlows = new();
    private readonly List<OmniLight3D> _engineLights = new();
    private Node3D? _modelRoot;
    private float _glowPhase;

    public ShipView(uint id, bool isPlayer, string name, bool highQuality)
    {
        Id = id;
        IsPlayer = isPlayer;
        DisplayName = name;
        HighQuality = highQuality;
        Name = $"Ship_{id}";
        Build();
    }

    public uint Id { get; }
    public bool IsPlayer { get; }
    public string DisplayName { get; set; }
    public bool HighQuality { get; }
    public ShipTrack Track { get; } = new();
    public EntityKind Kind => Track.Kind;
    public bool Boosting { get; set; }

    public void UpdateVisual(Vec3d renderOrigin)
    {
        Track.Sample(Time.GetTicksMsec() / 1000.0 - (IsPlayer ? 0.05 : 0.11), renderOrigin);
        if (!Track.HasData)
            return;

        GlobalTransform = new Transform3D(new Basis(Track.Orientation), Track.RenderPosition);

        _glowPhase += 0.1f;
        var throttle = Mathf.Max(0.18f, Mathf.Abs(Track.Throttle));
        var glow = 0.55f + throttle * 0.8f + (Boosting ? 0.9f : 0f);
        var pulse = 1.0f + Mathf.Sin(_glowPhase * 2.1f) * 0.06f;
        foreach (var engine in _engineGlows)
            engine.Scale = new Vector3(glow, glow, glow) * pulse;
        foreach (var light in _engineLights)
            light.LightEnergy = 0.8f + glow * 0.9f;
    }

    public void SetExternalModelVisible(bool visible)
    {
        if (_modelRoot is not null)
            _modelRoot.Visible = visible;
        foreach (var glow in _engineGlows)
            glow.Visible = visible;
        foreach (var light in _engineLights)
            light.Visible = visible;
    }

    public static Basis BasisFromAngles(float yaw, float pitch)
    {
        var cy = Mathf.Cos(yaw);
        var sy = Mathf.Sin(yaw);
        var cp = Mathf.Cos(pitch);
        var sp = Mathf.Sin(pitch);
        var forward = new Vector3(-sy * cp, sp, -cy * cp);
        var right = new Vector3(cy, 0, -sy);
        var up = right.Cross(forward);
        return new Basis(right, up, -forward).Orthonormalized();
    }

    private void Build()
    {
        _modelRoot = new Node3D { Name = "ModelRoot" };
        AddChild(_modelRoot);

        var scene = ResourceLoader.Exists("res://assets/models/ship_player.glb")
            ? GD.Load<PackedScene>("res://assets/models/ship_player.glb")
            : null;

        if (scene is not null)
        {
            var model = scene.Instantiate<Node3D>();
            model.Name = "GlbModel";
            model.Scale = Vector3.One * (IsPlayer ? 1.0f : 0.82f);
            if (!IsPlayer)
                TintModel(model, Id);
            _modelRoot.AddChild(model);
        }
        else
        {
            BuildFallbackHull(_modelRoot);
        }

        var glowMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.35f, 0.85f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.8f, 1.0f),
            EmissionEnergyMultiplier = 6.0f,
        };

        var engineScale = IsPlayer ? 1.0f : 0.82f;
        foreach (var x in new[] { -0.78f, 0.78f })
        {
            var glow = new MeshInstance3D
            {
                Name = $"EngineGlow_{x}",
                Mesh = new SphereMesh { Radius = 0.32f, Height = 0.64f, RadialSegments = 12, Rings = 8 },
                MaterialOverride = glowMaterial,
                Position = new Vector3(x * engineScale, 0, 4.52f * engineScale),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _modelRoot.AddChild(glow);
            _engineGlows.Add(glow);

            if (IsPlayer && HighQuality)
            {
                var light = new OmniLight3D
                {
                    Name = $"EngineLight_{x}",
                    Position = new Vector3(x * engineScale, 0, 4.3f * engineScale),
                    LightColor = new Color(0.3f, 0.75f, 1.0f),
                    LightEnergy = 1.2f,
                    OmniRange = 18,
                    ShadowEnabled = false,
                };
                _modelRoot.AddChild(light);
                _engineLights.Add(light);
            }
        }
    }

    private static void TintModel(Node3D model, uint id)
    {
        var palette = new[]
        {
            new Color(0.55f, 0.75f, 0.85f),
            new Color(0.85f, 0.70f, 0.50f),
            new Color(0.68f, 0.55f, 0.85f),
            new Color(0.55f, 0.85f, 0.70f),
        };
        var tint = palette[id % palette.Length];

        foreach (var meshInstance in model.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            if (meshInstance.Mesh is null)
                continue;
            var material = meshInstance.Mesh.SurfaceGetMaterial(0);
            if (material is StandardMaterial3D standard)
            {
                var duplicate = (StandardMaterial3D)standard.Duplicate();
                duplicate.AlbedoColor = duplicate.AlbedoColor.Lerp(tint, 0.55f);
                duplicate.EmissionEnabled = false;
                meshInstance.MaterialOverride = duplicate;
            }
        }
    }

    private static void BuildFallbackHull(Node3D parent)
    {
        var hull = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.27f, 0.35f),
            Metallic = 0.65f,
            Roughness = 0.35f,
        };
        var dark = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.06f, 0.07f, 0.09f),
            Metallic = 0.8f,
            Roughness = 0.4f,
        };

        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.4f, 0.5f, 7.5f) },
            MaterialOverride = hull,
        });
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(7.4f, 0.16f, 3.4f) },
            MaterialOverride = hull,
            Position = new Vector3(0, 0, 1.1f),
        });
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.25f, 1.6f) },
            MaterialOverride = dark,
            Position = new Vector3(0, 0.35f, 1.7f),
        });
    }
}
