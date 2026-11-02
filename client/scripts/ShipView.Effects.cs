using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared;

namespace IndifferentStars.Client;

public sealed partial class ShipView
{
    private readonly List<(Node3D Nozzle, Vector3 Exhaust)> _maneuverJets = new();
    private IonTrail _ionTrail = null!;
    private Vector3 _previousVelocity;
    private Quaternion _previousOrientation = Quaternion.Identity;
    private bool _effectsReady;
    private bool _particles = true;
    public bool EngineBraking { get; private set; }

    private void BuildManeuverEffects()
    {
        _ionTrail = new IonTrail();
        AddChild(_ionTrail);
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(0.35f, 0.75f, 1, 0.65f),
        };
        var mesh = new CylinderMesh { BottomRadius = 0.13f, TopRadius = 0.01f, Height = 1, RadialSegments = 8 };
        var scale = IsPlayer ? 1f : 0.82f;
        foreach (var sign in new[] { -1f, 1f })
        {
            // Retro-thrusters, side ports and dorsal/ventral pairs also provide visible rotation torque.
            AddJet(new Vector3(sign * 0.85f, 0, -3.5f), Vector3.Forward);
            foreach (var z in new[] { -1.5f, 2.2f })
            {
                AddJet(new Vector3(sign * 3.4f, 0, z), Vector3.Right * sign);
                foreach (var y in new[] { -1f, 1f })
                    AddJet(new Vector3(sign * 2f, y * 0.3f, z), Vector3.Up * y);
            }
        }

        void AddJet(Vector3 position, Vector3 exhaust)
        {
            var nozzle = new Node3D
            {
                Name = $"ManeuverJet_{_maneuverJets.Count}",
                Position = position * scale,
                Basis = Basis.LookingAt(-exhaust, Mathf.Abs(exhaust.Y) > 0.9f ? Vector3.Right : Vector3.Up)
                    * new Basis(Vector3.Right, Mathf.Pi / 2),
                Visible = false,
            };
            nozzle.AddChild(new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = material, Position = Vector3.Up * 0.5f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _modelRoot!.AddChild(nozzle);
            _maneuverJets.Add((nozzle, exhaust));
        }
    }

    private void UpdateManeuverEffects(float delta, Vec3d renderOrigin, float thrust)
    {
        var acceleration = Vector3.Zero;
        var angular = Vector3.Zero;
        if (_effectsReady && delta > 0 && delta < 0.25f)
        {
            acceleration = Basis.Inverse() * ((Track.Velocity - _previousVelocity) / delta);
            angular = (_previousOrientation.Inverse() * Track.Orientation).GetEuler() / delta;
        }
        _effectsReady = true;
        _previousVelocity = Track.Velocity;
        _previousOrientation = Track.Orientation;
        var localVelocity = Basis.Inverse() * Track.Velocity;
        EngineBraking = localVelocity.LengthSquared() > 1 && acceleration.Dot(localVelocity) < -100;
        foreach (var (nozzle, exhaust) in _maneuverJets)
        {
            var torque = nozzle.Position.Cross(-exhaust).Normalized();
            var load = Mathf.Clamp(acceleration.Dot(-exhaust) / 120 + angular.Dot(torque) * 0.8f, 0, 1);
            nozzle.Visible = _externalVisible && _engineEffects && !Track.Docked && load > 0.06f;
            nozzle.Scale = new Vector3(1, 0.15f + load * 1.6f, 1);
        }
        _ionTrail.Visible = _externalVisible && _engineEffects && _particles && !Track.Docked;
        var scale = IsPlayer ? 1f : 0.82f;
        var left = Basis * new Vector3(-0.78f, 0, 4.52f) * scale;
        var right = Basis * new Vector3(0.78f, 0, 4.52f) * scale;
        _ionTrail.UpdateTrail(delta, renderOrigin,
            Track.WorldPosition + new Vec3d(left.X, left.Y, left.Z),
            Track.WorldPosition + new Vec3d(right.X, right.Y, right.Z), thrust > 0.05f);
    }
}
