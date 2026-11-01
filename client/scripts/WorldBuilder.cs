using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

/// <summary>
/// Builds the static, beautiful part of the M0 system from the shared catalog.
/// Only original procedural geometry and Blender-generated GLB assets are used.
/// Celestial bodies are managed by <see cref="CelestialRenderer"/> so physical scale
/// and display scale stay separate.
/// </summary>
public sealed partial class WorldBuilder
{
    private readonly Node3D _root;
    private readonly bool _highQuality;
    private Node3D? _stationNode;
    private float _elapsed;
    private StandardMaterial3D? _beaconMaterial;
    private WorldEnvironment? _worldEnvironment;
    private CelestialRenderer? _celestial;

    public WorldBuilder(Node3D root, WorldCatalog catalog, bool highQuality)
    {
        _root = root;
        Catalog = catalog;
        _highQuality = highQuality;
    }

    public WorldCatalog Catalog { get; }
    public Node3D StationNode { get; private set; } = null!;
    public float RequiredFarPlane => _celestial?.RequiredFarPlane ?? 800_000f;

    public void Build()
    {
        BuildEnvironment();
        _celestial = new CelestialRenderer(_root, Catalog, _highQuality);
        BuildStation();
        BuildAsteroidBelt();
        BuildLights();
    }

    public void Process(double delta, Vector3 observerLocal)
    {
        _elapsed += (float)delta;
        _celestial?.Update(observerLocal, (float)delta);

        if (_stationNode is not null)
            _stationNode.RotateY((float)delta * 0.012f);
        if (_beaconMaterial is not null)
        {
            var pulse = 0.65f + 0.35f * Mathf.Sin(_elapsed * 2.4f);
            _beaconMaterial.EmissionEnergyMultiplier = 2.5f + pulse * 4.0f;
        }
    }

    public void SetGlowEnabled(bool enabled)
    {
        if (_worldEnvironment?.Environment is { } environment)
            environment.GlowEnabled = enabled;
    }

    private void BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.32f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.95f,
            GlowEnabled = _highQuality,
            GlowIntensity = 0.55f,
            GlowStrength = 0.9f,
            GlowBloom = 0.06f,
            GlowHdrThreshold = 1.1f,
        };

        var skyMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/shaders/sky.gdshader"),
        };
        skyMaterial.SetShaderParameter("star_gain", 1.0f);
        environment.Sky = new Sky { SkyMaterial = skyMaterial };

        _worldEnvironment = new WorldEnvironment { Environment = environment };
        _root.AddChild(_worldEnvironment);
    }

    private void BuildStation()
    {
        var station = Catalog.Station;
        var node = new Node3D
        {
            Name = "StationKestrelReach",
            Position = ToVector3(station.Position),
            Rotation = new Vector3(0, station.Rotation, 0),
        };

        var scene = TryLoadScene("res://assets/models/station_kestrel.glb");
        if (scene is not null)
        {
            var model = scene.Instantiate<Node3D>();
            model.Name = "Model";
            model.Scale = Vector3.One * 55f;
            node.AddChild(model);
        }
        else
        {
            BuildFallbackStation(node);
        }

        _beaconMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.45f, 0.9f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.35f, 0.85f, 1.0f),
            EmissionEnergyMultiplier = 5.0f,
        };
        var beacon = new MeshInstance3D
        {
            Name = "Beacon",
            Mesh = new SphereMesh { Radius = 5.5f, Height = 11, RadialSegments = 16, Rings = 8 },
            MaterialOverride = _beaconMaterial,
            Position = new Vector3(0, 120, 0),
        };
        node.AddChild(beacon);

        _root.AddChild(node);
        _stationNode = node;
        StationNode = node;
    }

    private void BuildLights()
    {
        // Starlight itself comes from CelestialRenderer. This cool fill simulates
        // planet-shine and keeps unlit sides readable without flattening the image.
        var fill = new DirectionalLight3D
        {
            Name = "PlanetShine",
            LightColor = new Color(0.34f, 0.48f, 0.78f),
            LightEnergy = 0.42f,
            ShadowEnabled = false,
        };
        _root.AddChild(fill);
        fill.RotationDegrees = new Vector3(28, 205, 0);

        var rim = new DirectionalLight3D
        {
            Name = "RimLight",
            LightColor = new Color(0.85f, 0.72f, 0.55f),
            LightEnergy = 0.18f,
            ShadowEnabled = false,
        };
        _root.AddChild(rim);
        rim.RotationDegrees = new Vector3(-35, 35, 0);
    }

    private static PackedScene? TryLoadScene(string path) =>
        ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;

    private static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
}