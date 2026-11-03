using System;
using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

/// <summary>
/// Renders celestial bodies that are physically far away but must look large.
///
/// Physics stays in system coordinates (metres, double). This renderer projects a body
/// into the local display frame while preserving its angular size from the observer:
/// a huge planet may be drawn at a smaller scale, but both its distance and radius are
/// scaled by the same factor, so the visible size and the framing are honest. When the
/// observer actually gets close, the projection switches to true scale.
///
/// The observer position is re-evaluated every frame, so the direction and apparent size
/// update as the player moves; nothing is parented to the starting station.
/// </summary>
public sealed class CelestialRenderer
{
    private sealed class CelestialVisual
    {
        public required CelestialBody Body { get; init; }
        public required Node3D Holder { get; init; }
        public required MeshInstance3D Surface { get; init; }
        public float AngularGain { get; init; } = 1f;
        public float SpinSpeed { get; init; }
        /// <summary>When > 0, place the body at this display distance (infinite-distance sky object).</summary>
        public double FixedDisplayDistance { get; init; }
        public double DisplayDistance { get; set; }
        public double DisplayRadius { get; set; }
        public bool TrueScale { get; set; }
        public double SurfaceDistance { get; set; }
    }

    private const double TrueScaleSurfaceDistance = 100_000.0; // 100 km: switch to real geometry
    private const float MinimumSurfaceClearance = 18_000f;

    private readonly Node3D _root;
    private readonly WorldCatalog _catalog;
    private readonly bool _highQuality;
    private readonly List<CelestialVisual> _visuals = new();
    private readonly Node3D _celestialRoot;
    private readonly DirectionalLight3D _sunLight;
    private float _elapsed;

    public CelestialRenderer(Node3D root, WorldCatalog catalog, bool highQuality)
    {
        _root = root;
        _catalog = catalog;
        _highQuality = highQuality;

        _celestialRoot = new Node3D { Name = "CelestialBodies", TopLevel = true };
        _root.AddChild(_celestialRoot);

        _sunLight = new DirectionalLight3D
        {
            Name = "Starlight",
            LightColor = new Color(1.0f, 0.92f, 0.80f),
            LightEnergy = 1.35f,
            LightAngularDistance = 0.6f,
            ShadowEnabled = false,
        };
        _root.AddChild(_sunLight);

        BuildPlanet(catalog.Planet);
        BuildStar(catalog.Star);

        Update(Vec3d.Zero, Vec3d.Zero, 0f);
    }

    /// <summary>Far plane needed by the camera to include every projected body.</summary>
    public float RequiredFarPlane { get; private set; } = 800_000f;

    public Node3D PlanetHolder => _visuals.Count > 0 ? _visuals[0].Holder : _celestialRoot;
    public float SunEnergy { get => _sunLight.LightEnergy; set => _sunLight.LightEnergy = value; }
    public bool ShadowsEnabled { get => _sunLight.ShadowEnabled; set => _sunLight.ShadowEnabled = value; }

    public void Update(Vec3d observerLocal, Vec3d renderOrigin, float delta)
    {
        _elapsed += delta;

        var observerSystem = _catalog.LocalFrameOrigin + observerLocal;
        var observer = observerLocal - renderOrigin;
        var far = 0f;

        foreach (var visual in _visuals)
        {
            var body = visual.Body;
            var offset = body.SystemPosition - observerSystem;
            var distance = Math.Max(1.0, offset.Length);
            var direction = offset / distance;
            var surfaceDistance = distance - body.Radius;

            // Future transition: when a player really approaches the body, draw it at true
            // scale in sector coordinates instead of a scaled distant projection.
            // Keep the stellar disc in the bounded sky projection: a billion-metre
            // render frustum loses the foreground ship and breaks float plane extraction.
            visual.TrueScale = body.Kind != CelestialKind.Star && surfaceDistance <= TrueScaleSurfaceDistance;
            visual.SurfaceDistance = surfaceDistance;

            Vector3 position;
            double displayRadius;
            double displayDistance;

            if (visual.TrueScale)
            {
                position = ToVector3(body.SystemPosition - _catalog.LocalFrameOrigin - renderOrigin);
                displayRadius = body.Radius;
                displayDistance = distance;
            }
            else if (visual.FixedDisplayDistance > 0)
            {
                var angular = Math.Min(1.45, Math.Asin(Math.Clamp(body.Radius / distance, 0, 1)) * visual.AngularGain);
                displayDistance = visual.FixedDisplayDistance;
                displayRadius = Math.Sin(angular) * displayDistance;
                position = ToVector3(observer) + ToVector3(direction) * (float)displayDistance;
            }
            else
            {
                var angular = Math.Min(1.45, Math.Asin(Math.Clamp(body.Radius / distance, 0, 1)) * visual.AngularGain);
                var sin = Math.Sin(angular);
                var clearance = Math.Max(MinimumSurfaceClearance, 17_850f);
                displayRadius = clearance * sin / Math.Max(0.0001, 1.0 - sin);
                displayDistance = displayRadius + clearance;
                position = ToVector3(observer) + ToVector3(direction) * (float)displayDistance;
            }

            visual.DisplayRadius = displayRadius;
            visual.DisplayDistance = displayDistance;
            visual.Holder.Position = position;
            visual.Holder.Scale = Vector3.One * (float)displayRadius;

            if (visual.SpinSpeed != 0)
                visual.Surface.RotateY(visual.SpinSpeed * delta);

            var extent = displayDistance + displayRadius * Math.Max(1.0, body.RingOuterRadii) * 1.15;
            far = Math.Max(far, (float)extent);
        }

        RequiredFarPlane = Math.Max(400_000f, far * 1.25f);

        // Directional light arrives from the star; computed in real system coordinates.
        var lightDirection = ToVector3((observerSystem - _catalog.Star.SystemPosition).Normalized());
        var lightUp = Mathf.Abs(lightDirection.Dot(Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;
        _sunLight.Basis = Basis.LookingAt(lightDirection, lightUp);
    }

    private void BuildStar(CelestialBody star)
    {
        var holder = new Node3D
        {
            Name = "StarVesper",
        };
        _celestialRoot.AddChild(holder);

        var surface = new MeshInstance3D
        {
            Name = "Photosphere",
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1.0f, 0.82f, 0.52f),
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.72f, 0.38f),
                EmissionEnergyMultiplier = 42f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        holder.AddChild(surface);

        var coronaMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoTexture = CreateRadialGlowTexture(
                new Color(1.0f, 0.90f, 0.72f, 0.42f),
                new Color(1.0f, 0.58f, 0.22f, 0.30f)),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            AlbedoColor = new Color(1.0f, 0.86f, 0.62f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.66f, 0.30f),
            EmissionEnergyMultiplier = 3.2f,
        };

        var corona = new MeshInstance3D
        {
            Name = "Corona",
            Mesh = new QuadMesh { Size = new Vector2(18f, 18f) },
            MaterialOverride = coronaMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        holder.AddChild(corona);

        _visuals.Add(new CelestialVisual
        {
            Body = star,
            Holder = holder,
            Surface = surface,
            // A star is a point of light at one AU. A small artistic gain keeps its
            // photosphere legible as a disc without turning it into a red giant.
            AngularGain = 3.0f,
            FixedDisplayDistance = 150_000,
        });
    }

    private void BuildPlanet(CelestialBody planet)
    {
        var holder = new Node3D
        {
            Name = "PlanetAsterReach",
            Rotation = new Vector3(planet.AxialTilt, 0, 0.12f),
        };
        _celestialRoot.AddChild(holder);

        var surface = new MeshInstance3D
        {
            Name = "Surface",
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 96, Rings = 64 },
            MaterialOverride = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://assets/shaders/planet.gdshader"),
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        holder.AddChild(surface);

        if (planet.AtmosphereHeight > 0)
        {
            var shell = 1f + (float)(planet.AtmosphereHeight / planet.Radius);
            var atmosphereMaterial = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://assets/shaders/atmosphere.gdshader"),
            };
            atmosphereMaterial.SetShaderParameter("atmosphere_color", new Color(0.38f, 0.60f, 1.0f));
            atmosphereMaterial.SetShaderParameter("strength", 1.15f);
            atmosphereMaterial.SetShaderParameter("sun_direction", holder.Basis.Inverse() *
                ToVector3((_catalog.Star.SystemPosition - planet.SystemPosition).Normalized()));
            holder.AddChild(new MeshInstance3D
            {
                Name = "Atmosphere",
                Mesh = new SphereMesh
                {
                    Radius = shell,
                    Height = shell * 2,
                    RadialSegments = 64,
                    Rings = 40,
                },
                MaterialOverride = atmosphereMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        if (planet.RingOuterRadii > 0)
        {
            var ringMaterial = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://assets/shaders/ring.gdshader"),
            };
            ringMaterial.SetShaderParameter("opacity", _highQuality ? 0.8f : 0.55f);
            holder.AddChild(new MeshInstance3D
            {
                Name = "Rings",
                Mesh = BuildRingMesh(planet.RingInnerRadii, planet.RingOuterRadii, 220),
                MaterialOverride = ringMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        _visuals.Add(new CelestialVisual
        {
            Body = planet,
            Holder = holder,
            Surface = surface,
            // Preserve angular growth during approach and through the true-scale transition.
            AngularGain = 1f,
            SpinSpeed = 0.0035f,
        });
    }

    private static ArrayMesh BuildRingMesh(double innerRadii, double outerRadii, int segments)
    {
        var inner = (float)innerRadii;
        var outer = (float)outerRadii;
        var vertices = new Vector3[(segments + 1) * 2];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var indices = new int[segments * 6];

        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var angle = t * Mathf.Tau;
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);

            vertices[i * 2] = new Vector3(cos * inner, 0, sin * inner);
            vertices[i * 2 + 1] = new Vector3(cos * outer, 0, sin * outer);
            normals[i * 2] = Vector3.Up;
            normals[i * 2 + 1] = Vector3.Up;
            uvs[i * 2] = new Vector2(0, t);
            uvs[i * 2 + 1] = new Vector2(1, t);
        }

        for (var i = 0; i < segments; i++)
        {
            var a = i * 2;
            var b = a + 1;
            var c = a + 2;
            var d = a + 3;
            var index = i * 6;
            indices[index] = a;
            indices[index + 1] = b;
            indices[index + 2] = c;
            indices[index + 3] = b;
            indices[index + 4] = d;
            indices[index + 5] = c;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static Texture2D CreateRadialGlowTexture(Color inner, Color outer)
    {
        var gradient = new Gradient
        {
            Colors = new[] { inner, outer, new Color(outer.R, outer.G, outer.B, 0.0f) },
            Offsets = new[] { 0.0f, 0.32f, 1.0f },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
        };
    }

    private static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
