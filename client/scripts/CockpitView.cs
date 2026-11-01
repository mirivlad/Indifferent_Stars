using Godot;

namespace IndifferentStars.Client;

/// <summary>
/// Minimal procedural cockpit interior. It exists only as client-side decoration;
/// the camera inside uses exactly the same authoritative ship transform as other views.
/// Geometry is laid out around an eye point at (0, 1.0, 0.1) looking down -Z, so the
/// dashboard sits in the lower part of the view and the forward view stays clear.
/// </summary>
public sealed partial class CockpitView : Node3D
{
    private Label3D _leftScreen = null!;
    private Label3D _centerScreen = null!;
    private Label3D _rightScreen = null!;

    private static readonly StandardMaterial3D HullMaterial = new()
    {
        AlbedoColor = new Color(0.10f, 0.115f, 0.14f),
        Metallic = 0.35f,
        Roughness = 0.62f,
    };

    private static readonly StandardMaterial3D FrameMaterial = new()
    {
        AlbedoColor = new Color(0.032f, 0.038f, 0.048f),
        Metallic = 0.5f,
        Roughness = 0.45f,
    };

    private static readonly StandardMaterial3D AccentMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = new Color(0.22f, 0.55f, 0.65f),
        EmissionEnabled = true,
        Emission = new Color(0.18f, 0.52f, 0.64f),
        EmissionEnergyMultiplier = 0.55f,
    };

    public CockpitView()
    {
        Name = "CockpitInterior";
        Build();
    }

    /// <summary>Eye position in ship-local metres. Shared with the camera code.</summary>
    public static readonly Vector3 EyePosition = new(0f, 1.0f, 0.1f);

    private void Build()
    {
        var screen = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.06f, 0.16f, 0.20f),
            EmissionEnabled = true,
            Emission = new Color(0.05f, 0.17f, 0.22f),
            EmissionEnergyMultiplier = 0.9f,
        };

        var warm = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.75f, 0.42f, 0.16f),
            EmissionEnabled = true,
            Emission = new Color(0.85f, 0.38f, 0.10f),
            EmissionEnergyMultiplier = 0.8f,
        };

        // Dashboard shelf running across the lower view, with a sloped panel under it.
        AddBox(new Vector3(3.2f, 0.08f, 0.62f), new Vector3(0, 0.5f, -1.3f), HullMaterial);
        AddBox(new Vector3(3.2f, 0.55f, 0.08f), new Vector3(0, 0.2f, -1.62f), HullMaterial, new Vector3(-26, 0, 0));
        AddBox(new Vector3(3.2f, 0.02f, 0.035f), new Vector3(0, 0.512f, -1.0f), AccentMaterial);
        AddBox(new Vector3(0.6f, 0.012f, 0.03f), new Vector3(0, 0.5f, -1.55f), warm);

        // Instrument screens stand just above the shelf lip so they stay visible
        // without covering the forward view.
        AddScreen(new Vector3(-0.86f, 0.63f, -1.44f), screen);
        AddScreen(new Vector3(0.86f, 0.63f, -1.44f), screen);
        AddScreen(new Vector3(0f, 0.60f, -1.45f), screen, new Vector3(0.62f, 0.13f, 0.02f));
        _leftScreen = AddInstrumentText(new Vector3(-0.86f, 0.66f, -1.425f), new Color(0.62f, 0.92f, 1.0f), 30);
        _centerScreen = AddInstrumentText(new Vector3(0f, 0.63f, -1.435f), new Color(1.0f, 0.80f, 0.48f), 26);
        _rightScreen = AddInstrumentText(new Vector3(0.86f, 0.66f, -1.425f), new Color(0.62f, 0.92f, 1.0f), 30);
        AddBox(new Vector3(0.02f, 0.13f, 0.02f), new Vector3(-0.86f, 0.55f, -1.44f), FrameMaterial);
        AddBox(new Vector3(0.02f, 0.13f, 0.02f), new Vector3(0.86f, 0.55f, -1.44f), FrameMaterial);

        // Side consoles run back past the eye, framing the view like real cockpit walls.
        AddBox(new Vector3(0.5f, 0.42f, 2.1f), new Vector3(-1.55f, 0.28f, -0.45f), HullMaterial);
        AddBox(new Vector3(0.5f, 0.42f, 2.1f), new Vector3(1.55f, 0.28f, -0.45f), HullMaterial);
        AddBox(new Vector3(0.06f, 0.015f, 1.85f), new Vector3(-1.55f, 0.5f, -0.45f), AccentMaterial);
        AddBox(new Vector3(0.06f, 0.015f, 1.85f), new Vector3(1.55f, 0.5f, -0.45f), AccentMaterial);

        // Canopy rim: thin edges near the frame border instead of a floating window.
        AddBox(new Vector3(4.2f, 0.10f, 0.10f), new Vector3(0, 1.85f, -1.7f), FrameMaterial);
        AddBox(new Vector3(0.09f, 1.4f, 0.12f), new Vector3(-1.82f, 1.2f, -1.5f), FrameMaterial, new Vector3(0, 0, -4));
        AddBox(new Vector3(0.09f, 1.4f, 0.12f), new Vector3(1.82f, 1.2f, -1.5f), FrameMaterial, new Vector3(0, 0, 4));

        // Central stick.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.035f, Height = 0.34f, RadialSegments = 10 },
            MaterialOverride = FrameMaterial,
            Position = new Vector3(0, 0.34f, -0.78f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.042f, Height = 0.084f, RadialSegments = 12, Rings = 8 },
            MaterialOverride = FrameMaterial,
            Position = new Vector3(0, 0.53f, -0.78f),
        });
        AddBox(new Vector3(0.1f, 0.012f, 0.012f), new Vector3(0, 0.55f, -0.78f), AccentMaterial);

        var instrumentLight = new OmniLight3D
        {
            Position = new Vector3(0, 0.55f, -1.25f),
            LightColor = new Color(0.32f, 0.68f, 0.82f),
            LightEnergy = 0.5f,
            OmniRange = 1.6f,
            ShadowEnabled = false,
        };
        AddChild(instrumentLight);

        var consoleLight = new OmniLight3D
        {
            Position = new Vector3(0, 0.9f, -0.2f),
            LightColor = new Color(0.55f, 0.28f, 0.14f),
            LightEnergy = 0.35f,
            OmniRange = 1.2f,
            ShadowEnabled = false,
        };
        AddChild(consoleLight);
    }

    /// <summary>
    /// Drives the three dashboard displays from the same snapshot data as the HUD.
    /// The cockpit therefore shows live instruments, not a decorative static screen.
    /// </summary>
    public void UpdateInstruments(
        float speed,
        float throttle,
        bool docked,
        string? targetName,
        float rangeMeters,
        float relativeAltitude,
        bool hasTarget,
        bool assist,
        bool autopilot,
        bool following)
    {
        if (!Visible)
            return;

        var throttleSign = throttle >= 0 ? "+" : "";
        _leftScreen.Text =
            Locale.Format("cockpit_speed", speed) + "\n" +
            Locale.Format("cockpit_throttle", throttleSign, throttle * 100);

        string mode;
        if (docked) mode = Locale.Text("cockpit_docked");
        else if (following) mode = Locale.Text("following");
        else if (autopilot) mode = Locale.Text("cockpit_autopilot");
        else mode = Locale.Text("cockpit_cruise");
        _centerScreen.Text = Locale.Format("cockpit_status", mode,
            assist ? Locale.Text("cockpit_assist") : "");

        if (hasTarget && !string.IsNullOrEmpty(targetName))
        {
            var sign = relativeAltitude >= 0 ? "+" : "";
            _rightScreen.Text =
                Locale.Format("cockpit_target", targetName.ToUpperInvariant()) + "\n" +
                Locale.Format("cockpit_range", rangeMeters / 1000f, sign, relativeAltitude);
        }
        else
        {
            _rightScreen.Text = Locale.Text("cockpit_no_target");
        }
    }

    private Label3D AddInstrumentText(Vector3 position, Color color, int fontSize)
    {
        var label = new Label3D
        {
            Text = "",
            Position = position,
            FontSize = fontSize,
            PixelSize = 0.0016f,
            Modulate = color,
            OutlineSize = 8,
            OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        AddChild(label);
        return label;
    }

    private void AddScreen(Vector3 position, Material material, Vector3? size = null)
    {
        AddBox(size ?? new Vector3(0.54f, 0.17f, 0.02f), position, material, new Vector3(-18, 0, 0));
    }

    private void AddBox(Vector3 size, Vector3 position, Material material, Vector3? rotationDegrees = null)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero,
        });
    }
}
