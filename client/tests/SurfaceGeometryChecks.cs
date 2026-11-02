using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Client;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;

public sealed partial class SurfaceGeometryChecks : Node3D
{
    public override void _Ready()
    {
        try
        {
            CollisionAssets.Validate();
            foreach (var (name, expected) in new[] { ("ship_player", SurfaceMesh.Ship), ("station_kestrel", SurfaceMesh.Station) })
            {
                var model = GD.Load<PackedScene>($"res://assets/models/{name}.glb").Instantiate<Node3D>();
                AddChild(model);
                var actual = new List<Triangle>();
                foreach (var mesh in model.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                {
                    var faces = mesh.Mesh.GetFaces();
                    Vec3d Point(Vector3 p)
                    {
                        p = mesh.GlobalTransform * p;
                        return new Vec3d(p.X, p.Y, p.Z);
                    }
                    for (var i = 0; i < faces.Length; i += 3)
                        actual.Add(new Triangle(Point(faces[i]), Point(faces[i + 1]), Point(faces[i + 2])));
                }
                Compare(name, expected, actual);
                model.QueueFree();
            }
            var asteroids = GD.Load<PackedScene>("res://assets/models/asteroids.glb").Instantiate<Node3D>();
            AddChild(asteroids);
            var variants = asteroids.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
            Check(variants.Length == SurfaceMesh.Asteroids.Length, "Asteroid variant count must match.");
            for (var variant = 0; variant < variants.Length; variant++)
            {
                var faces = variants[variant].Mesh.GetFaces();
                var actual = new List<Triangle>();
                Vec3d Point(Vector3 p) => new(p.X, p.Y, p.Z);
                for (var i = 0; i < faces.Length; i += 3)
                    actual.Add(new Triangle(Point(faces[i]), Point(faces[i + 1]), Point(faces[i + 2])));
                Compare("asteroid_" + variant, SurfaceMesh.Asteroids[variant], actual);
            }
            asteroids.QueueFree();
            GD.Print("SURFACE GEOMETRY CHECKS PASSED: all visible GLB triangles match server surfaces.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private static void Compare(string name, SurfaceMesh expected, List<Triangle> actual)
    {
        // Importers may reverse winding and introduce float rounding at node boundaries.
        static (long, long, long) Cell(Triangle t)
        {
            var center = (t.A + t.B + t.C) / 3;
            return ((long)Math.Floor(center.X * 1000), (long)Math.Floor(center.Y * 1000), (long)Math.Floor(center.Z * 1000));
        }
        static double Error(Triangle a, Triangle b)
        {
            var av = new[] { a.A, a.B, a.C };
            var bv = new[] { b.A, b.B, b.C };
            var best = double.PositiveInfinity;
            foreach (var permutation in new[] { new[] {0,1,2}, new[] {0,2,1}, new[] {1,0,2}, new[] {1,2,0}, new[] {2,0,1}, new[] {2,1,0} })
                best = Math.Min(best, Math.Max(Vec3d.Distance(av[0], bv[permutation[0]]),
                    Math.Max(Vec3d.Distance(av[1], bv[permutation[1]]), Vec3d.Distance(av[2], bv[permutation[2]]))));
            return best;
        }
        double largestError = 0;
        var cells = expected.Triangles.GroupBy(Cell).ToDictionary(group => group.Key, group => group.ToList());
        Check(actual.Count == expected.Triangles.Length, $"{name}: triangle counts differ.");
        foreach (var triangle in actual)
        {
            var (x, y, z) = Cell(triangle);
            List<Triangle>? selected = null;
            var selectedIndex = -1;
            var bestError = double.PositiveInfinity;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!cells.TryGetValue((x + dx, y + dy, z + dz), out var candidates)) continue;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var error = Error(triangle, candidates[i]);
                    if (error >= bestError) continue;
                    selected = candidates; selectedIndex = i; bestError = error;
                }
            }
            if (bestError >= 0.001)
            {
                var nearest = expected.Triangles.Select(t => (T: t, Error: Error(triangle, t))).OrderBy(x => x.Error).First();
                throw new InvalidOperationException($"Imported {name}: nearest surface error {nearest.Error:R} m; actual {triangle}; expected {nearest.T}.");
            }
            largestError = Math.Max(largestError, bestError);
            selected!.RemoveAt(selectedIndex);
        }
        GD.Print($"SURFACE VERIFIED: {name}, {actual.Count} triangles; largest importer deviation {largestError * 1000:F4} mm.");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
