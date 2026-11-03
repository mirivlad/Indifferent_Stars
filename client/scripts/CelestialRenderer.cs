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
    private readonly List<ShaderMaterial> _stellarMaterials = new();
    private ShaderMaterial _starMaterial = null!;
    private ShaderMaterial _coronaMaterial = null!;
    private Node3D _starHolder = null!;
    private float _baseSunEnergy = 1.35f;
    public float ExposureScale { get; private set; } = 1;

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
        foreach(var body in catalog.AdditionalCelestials)BuildPlanet(body);
        BuildStar();

        var stellarColor=catalog.Star.StellarClass switch {
            StellarClass.RedDwarf=>new Color(1,.5f,.3f),StellarClass.OrangeDwarf=>new Color(1,.75f,.5f),
            StellarClass.WhiteStar=>new Color(.85f,.92f,1),StellarClass.BlueStar=>new Color(.55f,.72f,1),_=>new Color(1,.92f,.8f)};
        _sunLight.LightColor=stellarColor;
        foreach(var material in _stellarMaterials)
        {
            material.SetShaderParameter("stellar_tint",stellarColor);
            material.SetShaderParameter("hot_star",(float)Math.Clamp((catalog.Star.TemperatureKelvin-6200)/6000,0,1));
        }
        Update(Vec3d.Zero, Vec3d.Zero, 0f);
    }

    /// <summary>Far plane needed by the camera to include every projected body.</summary>
    public float RequiredFarPlane { get; private set; } = 800_000f;

    public Node3D PlanetHolder => _visuals.Count > 0 ? _visuals[0].Holder : _celestialRoot;
    public float SunEnergy { get => _baseSunEnergy; set => _baseSunEnergy = value; }
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

            visual.TrueScale = surfaceDistance <= TrueScaleSurfaceDistance;
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
        UpdateStar(observerLocal, renderOrigin, delta);

        // Directional light arrives from the star; computed in real system coordinates.
        var lightDirection = ToVector3((observerSystem - _catalog.Star.SystemPosition).Normalized());
        var lightUp = Mathf.Abs(lightDirection.Dot(Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;
        _sunLight.Basis = Basis.LookingAt(lightDirection, lightUp);
    }

    private void BuildStar()
    {
        _starHolder = new Node3D { Name = "StarVesper" };
        _celestialRoot.AddChild(_starHolder);
        _starMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/star.gdshader") };
        _coronaMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/star_corona.gdshader") };
        _coronaMaterial.SetShaderParameter("volume_steps",_highQuality?12:6);
        _stellarMaterials.Add(_starMaterial);_stellarMaterials.Add(_coronaMaterial);
        foreach (var (name, material) in new[] { ("Photosphere", _starMaterial), ("Corona", _coronaMaterial) })
        {
            _starHolder.AddChild(new MeshInstance3D
            {
                Name = name,
                Mesh = new QuadMesh { Size = new Vector2(2,2) },
                MaterialOverride = material,
                ExtraCullMargin = 1_000_000,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        var anchors=new[]{new Vector3(.08f,.72f,.69f),new Vector3(.48f,-.86f,.15f),new Vector3(.5f,-.15f,-.85f),new Vector3(.86f,.4f,-.32f)};
        var tangents=new[]{new Vector3(0,-.69f,.72f),new Vector3(0,-.38f,-.92f),new Vector3(0,.96f,-.27f),new Vector3(.32f,0,.86f)};
        for(var i=0;i<anchors.Length;i++)
        for(var strand=-1;strand<=1;strand++)
        {
            var material=new ShaderMaterial {Shader=GD.Load<Shader>("res://assets/shaders/star_prominence.gdshader")};
            material.SetShaderParameter("anchor",anchors[i].Normalized());material.SetShaderParameter("tangent",tangents[i].Normalized());
            material.SetShaderParameter("strand",(float)strand);
            material.SetShaderParameter("phase",.7f+i*1.7f);_stellarMaterials.Add(material);
            // The vertex shader bends this UV grid into a closed strand in 3D stellar coordinates.
            _starHolder.AddChild(new MeshInstance3D {Name=$"Prominence_{i}_{strand+1}",Mesh=new PlaneMesh {Size=Vector2.One,SubdivideWidth=47,SubdivideDepth=7},
                MaterialOverride=material,ExtraCullMargin=1_000_000,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
    }

    public void SetStarDetail(bool high) => _coronaMaterial.SetShaderParameter("volume_steps",high?12:6);

    private void UpdateStar(Vec3d observerLocal, Vec3d renderOrigin, float delta)
    {
        var camera = _root.GetViewport().GetCamera3D();
        var cameraRender = camera?.GlobalPosition ?? ToVector3(observerLocal-renderOrigin);
        var cameraLocal = renderOrigin + new Vec3d(cameraRender.X,cameraRender.Y,cameraRender.Z);
        var offset = _catalog.Star.SystemPosition - _catalog.LocalFrameOrigin - cameraLocal;
        var distance = Math.Max(_catalog.Star.Radius + 1, offset.Length);
        _starHolder.Position = cameraRender;
        var proximity=(float)Math.Clamp((_catalog.Star.Radius/distance-.035)/.65,0,1);
        // Illumination depends on distance; only the smoothly adapting iris depends on the viewing direction.
        var luminosity=Math.Pow(_catalog.Star.Radius/696_000_000,2)*Math.Pow(_catalog.Star.TemperatureKelvin/5778,4);
        var irradiance=(float)Math.Clamp(Math.Pow(Math.Sqrt(luminosity)*149_597_870_700.0/distance,.55),.25,7);
        _sunLight.LightEnergy=_baseSunEnergy*irradiance;
        var facing=camera is null?0:Mathf.SmoothStep(0,1,Mathf.Clamp(((-camera.GlobalBasis.Z).Dot(ToVector3(offset.Normalized()))+.15f),0,1));
        var exposureTarget=1-proximity*(.22f+.2f*facing);
        var adaptation=exposureTarget<ExposureScale?1.6f:.35f;
        ExposureScale=Mathf.Lerp(ExposureScale,exposureTarget,1-Mathf.Exp(-delta*adaptation));
        foreach (var material in _stellarMaterials)
        {
            material.SetShaderParameter("star_direction",ToVector3(offset.Normalized()));
            material.SetShaderParameter("radius_ratio",(float)(_catalog.Star.Radius/distance));
            material.SetShaderParameter("surface_gap_ratio",(float)((distance-_catalog.Star.Radius)/distance));
            material.SetShaderParameter("distance_metres",(float)distance);
            material.SetShaderParameter("animation_time",_elapsed);
        }
    }

    private void BuildPlanet(CelestialBody planet)
    {
        var holder = new Node3D
        {
            Name = planet.Id==_catalog.Planet.Id?"PlanetAsterReach":$"Celestial_{planet.Id}",
            Rotation = new Vector3(planet.AxialTilt, 0, 0.12f),
        };
        _celestialRoot.AddChild(holder);

        var surface = new MeshInstance3D
        {
            Name = "Surface",
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 96, Rings = 64 },
            MaterialOverride = new ShaderMaterial
            {
                Shader = GD.Load<Shader>(planet.Surface==PlanetSurface.Gas?"res://assets/shaders/planet.gdshader":"res://assets/shaders/planet_solid.gdshader"),
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if(surface.MaterialOverride is ShaderMaterial terrain)
        {
            terrain.SetShaderParameter("surface_kind",(int)planet.Surface);
            terrain.SetShaderParameter("surface_seed",(float)planet.SurfaceSeed);
            if(planet.Surface==PlanetSurface.Gas)
            {
                var colors=new SeedStream(planet.SurfaceSeed,planet.Id,"gas-palette");
                terrain.SetShaderParameter("band_deep",new Color((float)colors.Between(.1,.35),(float)colors.Between(.12,.35),(float)colors.Between(.15,.4)));
                terrain.SetShaderParameter("band_light",new Color((float)colors.Between(.5,.8),(float)colors.Between(.45,.75),(float)colors.Between(.4,.7)));
            }
        }
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

    private static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
