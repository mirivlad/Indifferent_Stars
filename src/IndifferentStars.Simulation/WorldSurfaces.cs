using System;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Simulation;

public readonly record struct SurfacePose(SurfaceMesh Mesh, double Scale, Vec3d Position, Quaternion Rotation)
{
    public Vec3d Local(Vec3d point) => SurfaceMesh.Rotate(point - Position, Quaternion.Conjugate(Rotation)) / Scale;
    public double Radius => Mesh.Radius * Scale;
    public double Distance(Vec3d point)
    {
        var local = Local(point);
        return SurfaceQuery.Contains(Mesh, local) ? 0 : SurfaceQuery.Distance(Mesh, local) * Scale;
    }
}

public static class WorldSurfaces
{
    public static SurfacePose Ship(ShipState ship, Vec3d position, Quaternion rotation) =>
        new(SurfaceMesh.Ship, ship.IsPlayer ? 1 : ModelPose.NpcScale, position, rotation);

    public static SurfacePose Body(WorldCatalog catalog, StaticBody body, double time) =>
        body.Kind == BodyKind.Station
            ? new(SurfaceMesh.Station, ModelPose.StationScale, body.Position, ModelPose.StationRotation(body, time))
            : new(SurfaceMesh.Asteroids[body.Variant % SurfaceMesh.Asteroids.Length], ModelPose.AsteroidScale(body), body.Position, ModelPose.AsteroidRotation(body, catalog.Seed));

    public static double? Contact(SurfacePose fromA, SurfacePose toA, SurfacePose fromB, SurfacePose toB) =>
        SurfaceContact.Sweep(fromA.Mesh, fromA.Scale, fromA.Position, toA.Position, fromA.Rotation, toA.Rotation,
            fromB.Mesh, fromB.Scale, fromB.Position, toB.Position, fromB.Rotation, toB.Rotation);

    public static SurfaceHit? ContactHit(SurfacePose fromA, SurfacePose toA, SurfacePose fromB, SurfacePose toB) =>
        SurfaceContact.SweepHit(fromA.Mesh, fromA.Scale, fromA.Position, toA.Position, fromA.Rotation, toA.Rotation,
            fromB.Mesh, fromB.Scale, fromB.Position, toB.Position, fromB.Rotation, toB.Rotation, skipSeparating: true);

    public static SurfaceHit? SphereHit(SurfacePose from, SurfacePose to, Vec3d center, double radius)
    {
        var time = SphereContact(from,to,center,radius);
        if (time is not { } t) return null;
        var position = Vec3d.Lerp(from.Position,to.Position,t);
        var normal = (position-center).Normalized();
        if (t<=1e-6 && Vec3d.Dot(to.Position-from.Position,normal)>=0) return null;
        return new SurfaceHit(t,center+normal*radius,normal);
    }

    public static double? SphereContact(SurfacePose from, SurfacePose to, Vec3d center, double radius)
    {
        var travel = to.Position - from.Position;
        var offset = from.Position - center;
        var nearest = travel.LengthSquared > 0
            ? offset + travel * Math.Clamp(-Vec3d.Dot(offset, travel) / travel.LengthSquared, 0, 1) : offset;
        if (nearest.Length > radius + from.Radius) return null;
        double? first = null;
        // Celestial radii are millions of metres; a 5 cm bound also covers face curvature.
        var angle = 2 * Math.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(from.Rotation, to.Rotation)), 0, 1));
        var slices = Math.Max(1, (int)Math.Ceiling(angle * from.Radius / 0.1));
        for (var slice = 0; slice < slices; slice++)
        {
            var begin = (double)slice / slices; var end = (double)(slice + 1) / slices;
            var q = Quaternion.Slerp(from.Rotation, to.Rotation, (float)((begin + end) / 2));
            var pad = angle * from.Radius / (2 * slices) + 0.0001;
            foreach (var triangle in from.Mesh.Triangles)
            foreach (var vertex in new[] { triangle.A, triangle.B, triangle.C })
            {
                var local = SurfaceMesh.Rotate(vertex * from.Scale, q);
                var start = Vec3d.Lerp(from.Position, to.Position, begin) + local;
                var finish = Vec3d.Lerp(from.Position, to.Position, end) + local;
                var hit = CollisionSolver.ContactTime(start, finish, center, radius + pad);
                if (hit is not null && (first is null || (slice + hit) / slices < first)) first = (slice + hit) / slices;
            }
            if (first is not null) return first;
        }
        return null;
    }
}
