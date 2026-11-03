using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Client;

/// <summary>
/// Visual ship entity. Position and orientation always come from a SnapshotTrack;
/// this class never integrates movement itself.
/// </summary>
public sealed partial class ShipView : Node3D
{
    private readonly List<MeshInstance3D> _engineGlows = new();
    private readonly List<OmniLight3D> _engineLights = new();
    private readonly List<Node3D> _engineTrails = new();
    private readonly List<Vector3> _mainNozzles = new();
    private readonly List<(Vector3 Position, Vector3 Exhaust)> _maneuverSockets = new();
    private Node3D? _modelRoot;
    private float _glowPhase;
    private bool _externalVisible = true;
    private bool _engineEffects = true;

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

    public void UpdateVisual(Vec3d renderOrigin, float delta, double? frameTime = null)
    {
        Track.Sample((frameTime ?? Time.GetTicksUsec() / 1_000_000.0) - (IsPlayer ? 0.05 : 0.11), renderOrigin);
        if (!Track.HasData)
            return;

        GlobalTransform = new Transform3D(new Basis(Track.Orientation), Track.RenderPosition);

        _glowPhase += delta * 6;
        var warp = Track.WarpStage != WarpStage.None;
        if (_modelRoot is not null)
            _modelRoot.Scale = new Vector3(1, 1, Track.WarpStage == WarpStage.Travel ? 1.8f : 1);
        var throttle = Track.Docked ? 0 : warp ? 1 : Mathf.Max(0, Track.Throttle);
        var glow = 0.55f + throttle * 0.8f + (Boosting ? 0.9f : 0f);
        var pulse = 1.0f + Mathf.Sin(_glowPhase * 2.1f) * 0.06f;
        foreach (var engine in _engineGlows)
            engine.Scale = new Vector3(glow, glow, glow) * pulse;
        foreach (var light in _engineLights)
            light.LightEnergy = 0.8f + glow * 0.9f;

        var trailLength = 0.08f + throttle * 3.2f + (Boosting && throttle > 0 ? 4.2f : 0f);
        foreach (var trail in _engineTrails)
            trail.Scale = new Vector3(1f, trailLength, 1f);
        UpdateManeuverEffects(delta, renderOrigin, throttle);
    }

    public void SetExternalModelVisible(bool visible)
    {
        _externalVisible = visible;
        if (_modelRoot is not null)
            _modelRoot.Visible = visible;
        foreach (var glow in _engineGlows)
            glow.Visible = visible && _engineEffects;
        foreach (var light in _engineLights)
            light.Visible = visible && _engineEffects;
        foreach (var trail in _engineTrails)
            trail.Visible = visible && _engineEffects;
        foreach (var (nozzle, _) in _maneuverJets)
            if (!visible || !_engineEffects) nozzle.Visible = false;
        _ionTrail.Visible = visible && _engineEffects && _particles;
    }

    public void SetEngineEffects(bool enabled)
    {
        _engineEffects = enabled;
        SetExternalModelVisible(_externalVisible);
    }

    public void SetParticles(bool enabled)
    {
        _particles = enabled;
        _ionTrail.Visible = _externalVisible && _engineEffects && enabled;
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
            model.Scale = Vector3.One * (IsPlayer ? 1.0f : (float)ModelPose.NpcScale);
            if (!IsPlayer)
                TintModel(model, Id);
            _modelRoot.AddChild(model);
            foreach (var marker in model.FindChildren("MainNozzle_*", "Node3D", true, false).OfType<Node3D>().OrderBy(node => node.Name.ToString()))
                _mainNozzles.Add(ModelTransform(marker).Origin);
            foreach (var marker in model.FindChildren("Maneuver_*", "Node3D", true, false).OfType<Node3D>())
            {
                var transform = ModelTransform(marker);
                _maneuverSockets.Add((transform.Origin, -transform.Basis.Z.Normalized()));
            }
            if (_mainNozzles.Count != 2 || _maneuverSockets.Count != 14)
                throw new InvalidOperationException("The ship must export two main nozzles and fourteen maneuver outlets.");
        }
        else
        {
            throw new InvalidOperationException("The ship GLB is required for matching collision geometry.");
        }

        var glowMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.35f, 0.85f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.8f, 1.0f),
            EmissionEnergyMultiplier = 6.0f,
        };

        for (var index = 0; index < _mainNozzles.Count; index++)
        {
            var position = _mainNozzles[index];
            var glow = new MeshInstance3D
            {
                Name = $"EngineGlow_{index}",
                Mesh = new SphereMesh { Radius = 0.52f, Height = 1.04f, RadialSegments = 12, Rings = 8 },
                MaterialOverride = glowMaterial,
                Position = position,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _modelRoot.AddChild(glow);
            _engineGlows.Add(glow);

            var trailPivot = new Node3D
            {
                Name = $"EngineTrail_{index}",
                Position = position,
                RotationDegrees = new Vector3(90, 0, 0),
            };
            trailPivot.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.03f,
                    BottomRadius = 0.26f,
                    Height = 1f,
                    RadialSegments = 10,
                    Rings = 1,
                },
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    AlbedoColor = new Color(0.25f, 0.65f, 1f, 0.38f),
                    EmissionEnabled = true,
                    Emission = new Color(0.18f, 0.58f, 1f),
                    EmissionEnergyMultiplier = 3.2f,
                },
                Position = new Vector3(0, 0.5f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _modelRoot.AddChild(trailPivot);
            _engineTrails.Add(trailPivot);

            if (IsPlayer && HighQuality)
            {
                var light = new OmniLight3D
                {
                    Name = $"EngineLight_{index}",
                    Position = position - Vector3.Back * .22f,
                    LightColor = new Color(0.3f, 0.75f, 1.0f),
                    LightEnergy = 1.2f,
                    OmniRange = 18,
                    ShadowEnabled = false,
                };
                _modelRoot.AddChild(light);
                _engineLights.Add(light);
            }
        }
        BuildManeuverEffects();
    }

    private Transform3D ModelTransform(Node3D marker)
    {
        var transform = marker.Transform;
        for (var parent = marker.GetParent() as Node3D; parent is not null && parent != _modelRoot; parent = parent.GetParent() as Node3D)
            transform = parent.Transform * transform;
        return transform;
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
            if (material is StandardMaterial3D standard && standard.ResourceName == "Paint")
            {
                var duplicate = (StandardMaterial3D)standard.Duplicate();
                duplicate.AlbedoColor = duplicate.AlbedoColor.Lerp(tint, 0.55f);
                duplicate.EmissionEnabled = false;
                meshInstance.MaterialOverride = duplicate;
            }
        }
    }

}
