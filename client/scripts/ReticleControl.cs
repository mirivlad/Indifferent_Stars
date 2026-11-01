using Godot;

namespace IndifferentStars.Client;

public sealed partial class ReticleControl : Control
{
    public bool ShowRangeRing { get; set; } = true;
    public Color ReticleColor { get; set; } = new(0.55f, 0.85f, 1.0f, 0.72f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var center = Size * 0.5f;
        DrawArc(center, 24, 0, Mathf.Tau, 40, new Color(ReticleColor, 0.25f), 1.0f);
        DrawArc(center, 2.2f, 0, Mathf.Tau, 12, ReticleColor, 1.2f);

        DrawLine(center + new Vector2(-48, 0), center + new Vector2(-13, 0), ReticleColor, 1.2f);
        DrawLine(center + new Vector2(13, 0), center + new Vector2(48, 0), ReticleColor, 1.2f);
        DrawLine(center + new Vector2(0, -48), center + new Vector2(0, -13), ReticleColor, 1.2f);
        DrawLine(center + new Vector2(0, 13), center + new Vector2(0, 48), ReticleColor, 1.2f);

        DrawLine(center + new Vector2(-24, -24), center + new Vector2(-31, -31), new Color(ReticleColor, 0.5f), 1.0f);
        DrawLine(center + new Vector2(24, -24), center + new Vector2(31, -31), new Color(ReticleColor, 0.5f), 1.0f);
        DrawLine(center + new Vector2(-24, 24), center + new Vector2(-31, 31), new Color(ReticleColor, 0.5f), 1.0f);
        DrawLine(center + new Vector2(24, 24), center + new Vector2(31, 31), new Color(ReticleColor, 0.5f), 1.0f);

        if (ShowRangeRing)
            DrawArc(center, 74, -0.28f, 0.28f, 24, new Color(ReticleColor, 0.35f), 1.0f);
    }
}