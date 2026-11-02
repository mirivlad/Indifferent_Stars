using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared;

namespace IndifferentStars.Client;

/// <summary>Short-lived exhaust samples in simulation coordinates, independent of the hull and render origin.</summary>
public sealed partial class IonTrail : MeshInstance3D
{
    private readonly record struct Sample(Vec3d Left, Vec3d Right, float Age);
    private const float Lifetime = 0.3f;
    private readonly List<Sample> _samples = new(32);
    private readonly ImmediateMesh _mesh = new();
    private readonly ShaderMaterial _material = new()
    {
        Shader = GD.Load<Shader>("res://assets/shaders/ion_trail.gdshader"),
    };

    public IonTrail()
    {
        Name = "WorldIonTrail";
        TopLevel = true;
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
    }

    public void UpdateTrail(float delta, Vec3d renderOrigin, Vec3d left, Vec3d right, bool emitting)
    {
        GlobalTransform = Transform3D.Identity;
        _mesh.ClearSurfaces();
        if (!Visible || (_samples.Count > 0 && Vec3d.Distance(left, _samples[^1].Left) > 5000))
            _samples.Clear();
        for (var i = _samples.Count - 1; i >= 0; i--)
        {
            var sample = _samples[i] with { Age = _samples[i].Age + delta };
            if (sample.Age >= Lifetime) _samples.RemoveAt(i);
            else _samples[i] = sample;
        }
        if (Visible && emitting && (_samples.Count == 0 || Vec3d.Distance(left, _samples[^1].Left) > 0.1))
        {
            if (_samples.Count >= 32) _samples.RemoveAt(0);
            _samples.Add(new Sample(left, right, 0));
        }
        if (_samples.Count < 2) return;
        var camera = GetViewport().GetCamera3D();
        var cameraRight = camera?.GlobalBasis.X ?? Vector3.Right;
        var cameraPosition = camera?.GlobalPosition ?? Vector3.Zero;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);
        for (var i = 1; i < _samples.Count; i++)
        {
            AddRibbon(_samples[i - 1].Left, _samples[i].Left, _samples[i - 1].Age, _samples[i].Age, renderOrigin, cameraRight, cameraPosition);
            AddRibbon(_samples[i - 1].Right, _samples[i].Right, _samples[i - 1].Age, _samples[i].Age, renderOrigin, cameraRight, cameraPosition);
        }
        _mesh.SurfaceEnd();
    }

    private void AddRibbon(Vec3d from, Vec3d to, float fromAge, float toAge, Vec3d origin, Vector3 cameraRight, Vector3 cameraPosition)
    {
        var a = ShipTrack.ToVector3(from - origin);
        var b = ShipTrack.ToVector3(to - origin);
        var side = (b - a).Cross(cameraPosition - (a + b) / 2).Normalized();
        if (side.LengthSquared() < 0.5f) side = cameraRight;
        var wa = side * (0.12f + fromAge * 0.7f);
        var wb = side * (0.12f + toAge * 0.7f);
        Vertex(a - wa, fromAge, 0); Vertex(a + wa, fromAge, 1); Vertex(b + wb, toAge, 1);
        Vertex(a - wa, fromAge, 0); Vertex(b + wb, toAge, 1); Vertex(b - wb, toAge, 0);
    }

    private void Vertex(Vector3 position, float age, float edge)
    {
        var fade = 1 - age / Lifetime;
        _mesh.SurfaceSetColor(new Color(0.15f, 0.62f, 1, fade * fade * 0.42f));
        _mesh.SurfaceSetUV(new Vector2(edge, age / Lifetime));
        _mesh.SurfaceAddVertex(position);
    }
}
