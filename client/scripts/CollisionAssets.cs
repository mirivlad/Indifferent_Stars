using System;
using System.Linq;
using Godot;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public static class CollisionAssets
{
    public static void Validate()
    {
        var hulls = HullCatalog.All.Select(hull => (hull.Model, hull.Surface));
        foreach (var (name, mesh) in hulls.Concat(new[]
        {
            ("station_kestrel", SurfaceMesh.Station),
            ("asteroids", SurfaceMesh.Asteroids[0]),
        }))
        {
            if (Godot.FileAccess.GetSha256($"res://assets/models/{name}.glb") != mesh.SourceHash)
                throw new InvalidOperationException($"Collision surface mismatch for {name}. Run tools/art/export_collision_meshes.py and rebuild.");
        }
    }
}
