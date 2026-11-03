using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Shared.Collision;

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
    private WorldEnvironment? _worldEnvironment;
    private CelestialRenderer? _celestial;
    private ShaderMaterial? _skyMaterial;
    private readonly Dictionary<string, Node3D> _asteroidSectors = new();
    private Mesh[]? _asteroidMeshes;
    public double SimulationTime { get; set; }
    public int LoadedAsteroidSectors => _asteroidSectors.Count;

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
        UpdateAsteroidSectors(Vec3d.Zero);
        BuildLights();
    }

    public void Process(double delta, Vec3d observerLocal, Vec3d renderOrigin, double? serverSeconds = null)
    {
        SimulationTime = serverSeconds ?? SimulationTime + delta;
        _celestial?.Update(observerLocal, renderOrigin, (float)delta);
        if(_worldEnvironment?.Environment is { } environment&&_celestial is not null)
        {
            environment.TonemapExposure=.95f*_celestial.ExposureScale;
        }
        UpdateAsteroidSectors(observerLocal);

        if (_stationNode is not null)
        {
            var rotation = ModelPose.StationRotation(Catalog.Station, SimulationTime);
            _stationNode.Quaternion = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }
    }

    private void UpdateAsteroidSectors(Vec3d observer)
    {
        foreach (var sector in Catalog.Sectors)
        {
            var distance = Vec3d.Distance(observer, sector.Center) - sector.Radius;
            if (_asteroidSectors.TryGetValue(sector.Id, out var loaded))
            {
                if (distance > WorldCatalog.RelevanceRange + 2000)
                {
                    _asteroidSectors.Remove(sector.Id);
                    loaded.QueueFree();
                }
            }
            else if (distance <= WorldCatalog.RelevanceRange)
            {
                _asteroidMeshes ??= LoadAsteroidMeshes();
                var node = new Node3D { Name = sector.Id };
                _root.AddChild(node);
                BuildAsteroidBelt(node, sector.Bodies.Where(body => body.Kind == BodyKind.Asteroid), _asteroidMeshes);
                _asteroidSectors.Add(sector.Id, node);
            }
        }
    }

    public void SetGlowEnabled(bool enabled)
    {
        _celestial?.SetStarDetail(enabled);
        if (_worldEnvironment?.Environment is { } environment)
            environment.GlowEnabled = enabled;
    }

    public void SetVisualQuality(bool shadows, int skyDetail)
    {
        if (_celestial is not null) _celestial.ShadowsEnabled = shadows;
        foreach (var light in _stationLights)
            light.ShadowEnabled = shadows && light is SpotLight3D;
        _skyMaterial?.SetShaderParameter("detail", skyDetail);
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
        _skyMaterial = skyMaterial;
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
            model.Scale = Vector3.One * (float)ModelPose.StationScale;
            node.AddChild(model);
        }
        else
        {
            throw new InvalidOperationException("The station GLB is required for matching collision geometry.");
        }

        _root.AddChild(node);
        BuildStationLights(node);
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
