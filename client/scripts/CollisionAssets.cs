using System;
using Godot;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Client;

public static class CollisionAssets
{
    public static void Validate()
    {
        foreach (var (name, mesh) in new[]
        {
            ("ship_player", SurfaceMesh.Ship),
            ("station_kestrel", SurfaceMesh.Station),
            ("asteroids", SurfaceMesh.Asteroids[0]),
        })
        {
            if (Godot.FileAccess.GetSha256($"res://assets/models/{name}.glb") != mesh.SourceHash)
                throw new InvalidOperationException($"Collision surface mismatch for {name}. Run tools/art/export_collision_meshes.py and rebuild.");
        }
    }
}
