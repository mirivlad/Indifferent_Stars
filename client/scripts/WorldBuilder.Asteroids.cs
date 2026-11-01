using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public sealed partial class WorldBuilder
{
    private void BuildAsteroidBelt(Node3D parent, IEnumerable<StaticBody> bodies, Mesh[] meshes)
    {
        var groups = bodies.GroupBy(a => a.Variant % meshes.Length).ToArray();

        foreach (var group in groups)
        {
            var asteroids = group.ToArray();
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = meshes[group.Key],
                InstanceCount = asteroids.Length,
            };

            for (var i = 0; i < asteroids.Length; i++)
            {
                var body = asteroids[i];
                var random = new Random(unchecked(Catalog.Seed * 397 ^ body.Id));
                var rotation = new Vector3(
                    (float)(random.NextDouble() * Math.PI * 2),
                    body.Rotation,
                    (float)(random.NextDouble() * Math.PI * 2 - Math.PI));
                var scale = (float)(body.Radius * 1.08);
                var basis = Basis.FromEuler(rotation).Scaled(Vector3.One * scale);
                multiMesh.SetInstanceTransform(i, new Transform3D(basis, ToVector3(body.Position)));
            }

            var instance = new MultiMeshInstance3D
            {
                Name = $"AsteroidBelt_{group.Key}",
                Multimesh = multiMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            parent.AddChild(instance);
        }
    }

    private static Mesh[] LoadAsteroidMeshes()
    {
        var scene = TryLoadScene("res://assets/models/asteroids.glb");
        if (scene is not null)
        {
            var temp = scene.Instantiate<Node3D>();
            var meshes = temp.FindChildren("*", "MeshInstance3D", true, false)
                .OfType<MeshInstance3D>()
                .Select(m => m.Mesh)
                .Where(m => m is not null)
                .Cast<Mesh>()
                .Take(3)
                .ToArray();
            temp.Free();
            if (meshes.Length > 0)
                return meshes;
        }

        var fallback = new List<Mesh>();
        for (var i = 0; i < 3; i++)
        {
            fallback.Add(new SphereMesh
            {
                Radius = 1.0f,
                Height = 2.0f,
                RadialSegments = 9 + i * 2,
                Rings = 6 + i,
            });
        }
        return fallback.ToArray();
    }

    private static void BuildFallbackStation(Node3D parent)
    {
        var hull = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.58f, 0.62f),
            Metallic = 0.6f,
            Roughness = 0.4f,
        };
        var dark = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.07f, 0.08f, 0.10f),
            Metallic = 0.8f,
            Roughness = 0.35f,
        };
        var glow = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.45f, 0.9f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.8f, 1.0f),
            EmissionEnergyMultiplier = 6.0f,
        };

        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 65, BottomRadius = 65, Height = 150, RadialSegments = 32 },
            MaterialOverride = hull,
        });
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 165, OuterRadius = 210, Rings = 64, RingSegments = 24 },
            MaterialOverride = dark,
        });
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 8, Height = 16, RadialSegments = 12, Rings = 8 },
            MaterialOverride = glow,
            Position = new Vector3(0, 110, 0),
        });
    }
}
