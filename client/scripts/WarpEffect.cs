using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class WarpEffect : CanvasLayer
{
    private ShaderMaterial _material = null!;
    private ColorRect _surface = null!;
    private float _target;
    public float Intensity { get; private set; }

    public override void _Ready()
    {
        Layer = 7;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/warp.gdshader") };
        _surface = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore };
        _surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_surface);
    }

    public void SetState(WarpStage stage, float progress, bool tactical, bool highQuality)
    {
        _target = stage switch
        {
            WarpStage.Charging => Mathf.SmoothStep(0, 1, progress) * 0.8f,
            WarpStage.Travel => 1,
            WarpStage.Exit or WarpStage.Recovery => 1 - progress,
            _ => 0,
        };
        _material.SetShaderParameter("travel", stage == WarpStage.Travel ? 1f : 0f);
        _material.SetShaderParameter("tactical", tactical ? 1f : 0f);
        _material.SetShaderParameter("detail", highQuality ? 1f : 0f);
        _material.SetShaderParameter("aspect", _surface.Size.X / Mathf.Max(1, _surface.Size.Y));
    }

    public override void _Process(double delta)
    {
        Intensity = Mathf.Lerp(Intensity, _target, 1 - Mathf.Exp(-(float)delta * 12));
        _surface.Visible = Intensity > 0.002f;
        _material.SetShaderParameter("energy", Intensity);
    }
}
