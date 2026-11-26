using System;
using Godot;
using IndifferentStars.Shared;

namespace IndifferentStars.Client;

/// <summary>Short-lived exhaust samples in simulation coordinates, independent of the hull and render origin.</summary>
public sealed partial class IonTrail : MeshInstance3D
{
    private const float Lifetime = 0.3f;
    private const int SampleLimit = 32;
    private readonly Vec3d[] _positions;
    private readonly int _outletCount;
    private readonly double[] _sampleTimes = new double[SampleLimit];
    private double _time;
    private int _start;
    private int _count;
    private readonly ImmediateMesh _mesh = new();
    private readonly ShaderMaterial _material = new()
    {
        Shader = GD.Load<Shader>("res://assets/shaders/ion_trail.gdshader"),
    };

    public IonTrail(int outlets)
    {
        Name = "WorldIonTrail";
        TopLevel = true;
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        _outletCount = outlets;
        _positions = new Vec3d[SampleLimit * outlets];
    }

    public void UpdateTrail(float delta, Vec3d renderOrigin, ReadOnlySpan<Vec3d> outlets, bool emitting)
    {
        GlobalTransform = Transform3D.Identity;
        _mesh.ClearSurfaces();
        _time += delta;
        if (!Visible)
        {
            _count = 0;
            return;
        }
        if (_count > 0 && Vec3d.Distance(outlets[0], _positions[At(_count - 1) * _outletCount]) > 5000)
            _count = 0;
        while (_count > 0 && _time - _sampleTimes[_start] >= Lifetime)
            DropOldest();
        if (emitting && (_count == 0 || Vec3d.Distance(outlets[0], _positions[At(_count - 1) * _outletCount]) > 0.1))
        {
            if (_count == SampleLimit) DropOldest();
            var index = At(_count++);
            outlets.CopyTo(_positions.AsSpan(index * _outletCount, _outletCount));
            _sampleTimes[index] = _time;
        }
        if (_count < 2) return;
        var camera = GetViewport().GetCamera3D();
        var cameraRight = camera?.GlobalBasis.X ?? Vector3.Right;
        var cameraPosition = camera?.GlobalPosition ?? Vector3.Zero;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);
        for (var i = 1; i < _count; i++)
        {
            var a = At(i - 1);
            var b = At(i);
            var ageA = (float)(_time - _sampleTimes[a]);
            var ageB = (float)(_time - _sampleTimes[b]);
            for (var outlet = 0; outlet < _outletCount; outlet++)
                AddRibbon(_positions[a * _outletCount + outlet], _positions[b * _outletCount + outlet],
                    ageA, ageB, renderOrigin, cameraRight, cameraPosition);
        }
        _mesh.SurfaceEnd();
    }

    private int At(int offset) => (_start + offset) % SampleLimit;

    private void DropOldest()
    {
        _start = At(1);
        _count--;
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
