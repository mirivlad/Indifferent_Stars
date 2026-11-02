using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Shared.Collision;

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
                var rotation = ModelPose.AsteroidRotation(body, Catalog.Seed);
                var scale = (float)ModelPose.AsteroidScale(body);
                var basis = new Basis(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W)).Scaled(Vector3.One * scale);
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

        throw new InvalidOperationException("Asteroid GLB meshes are required for matching collision geometry.");
    }
}
