using System;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Simulation;

public static class SurfaceContact
{
    // Rotating surfaces are bounded to at most 5 cm deviation from each midpoint pose.
    public const double RotationTolerance = 0.05;

    public static double? Sweep(SurfaceMesh a, double scaleA, Vec3d startA, Vec3d endA, Quaternion fromA, Quaternion toA,
        SurfaceMesh b, double scaleB, Vec3d startB, Vec3d endB, Quaternion fromB, Quaternion toB)
    {
        var relativeStart = startA - startB;
        var relativeEnd = endA - endB;
        var radius = a.Radius * scaleA + b.Radius * scaleB;
        var motion = relativeEnd - relativeStart;
        var nearest = motion.LengthSquared > 0
            ? relativeStart + motion * Math.Clamp(-Vec3d.Dot(relativeStart, motion) / motion.LengthSquared, 0, 1) : relativeStart;
        if (nearest.Length > radius) return null;
        var arcA = Angle(fromA, toA) * a.Radius * scaleA;
        var arcB = Angle(fromB, toB) * b.Radius * scaleB;
        var slices = Math.Max(1, (int)Math.Ceiling((arcA + arcB) / (2 * RotationTolerance)));
        for (var slice = 0; slice < slices; slice++)
        {
            var begin = (double)slice / slices;
            var end = (double)(slice + 1) / slices;
            var mid = (float)((begin + end) * 0.5);
            var qa = Quaternion.Slerp(fromA, toA, mid);
            var inverseB = Quaternion.Conjugate(Quaternion.Slerp(fromB, toB, mid));
            var rotation = inverseB * qa;
            var offset = SurfaceMesh.Rotate(Vec3d.Lerp(relativeStart, relativeEnd, begin), inverseB) / scaleB;
            var delta = SurfaceMesh.Rotate(motion / slices, inverseB) / scaleB;
            var padding = (arcA + arcB) / (2 * slices * scaleB);
            double? first = null;
            Vec3d Transform(Vec3d point) => SurfaceMesh.Rotate(point * (scaleA / scaleB), rotation) + offset;
            Bounds TransformBounds(Bounds bounds)
            {
                var low = bounds.Min; var high = bounds.Max;
                var corner = Transform(low);
                var result = new Bounds(corner, corner);
                for (var mask = 1; mask < 8; mask++)
                {
                    corner = Transform(new Vec3d((mask & 1) == 0 ? low.X : high.X,
                        (mask & 2) == 0 ? low.Y : high.Y, (mask & 4) == 0 ? low.Z : high.Z));
                    result = result.Union(new Bounds(corner, corner));
                }
                return result.Union(result.Move(delta)).Expand(padding + 1e-6);
            }
            void Visit(SurfaceMesh.Node nodeA, Bounds boundsA, SurfaceMesh.Node nodeB)
            {
                if (first <= 1e-6 || !boundsA.Intersects(nodeB.Bounds)) return;
                if (nodeA.Left is not null && (nodeB.Left is null || nodeA.Count >= nodeB.Count))
                {
                    Visit(nodeA.Left, TransformBounds(nodeA.Left.Bounds), nodeB);
                    Visit(nodeA.Right!, TransformBounds(nodeA.Right!.Bounds), nodeB);
                    return;
                }
                if (nodeB.Left is not null)
                {
                    Visit(nodeA, boundsA, nodeB.Left);
                    Visit(nodeA, boundsA, nodeB.Right!);
                    return;
                }
                for (var ia = nodeA.Start; ia < nodeA.Start + nodeA.Count; ia++)
                {
                    var source = a.Triangles[ia];
                    var triangle = new Triangle(Transform(source.A), Transform(source.B), Transform(source.C));
                    for (var ib = nodeB.Start; ib < nodeB.Start + nodeB.Count; ib++)
                    {
                        var hit = Triangles(triangle, b.Triangles[ib], delta, padding);
                        if (hit is not null && (first is null || hit < first)) first = hit;
                    }
                }
            }
            Visit(a.Root, TransformBounds(a.Root.Bounds), b.Root);
            if (first is { } contact) return (slice + contact) / slices;
        }
        return null;
    }

    private static double Angle(Quaternion a, Quaternion b)
    {
        if (a == b) return 0;
        var dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W;
        var aa = (double)a.X * a.X + (double)a.Y * a.Y + (double)a.Z * a.Z + (double)a.W * a.W;
        var bb = (double)b.X * b.X + (double)b.Y * b.Y + (double)b.Z * b.Z + (double)b.W * b.W;
        return 2 * Math.Acos(Math.Clamp(Math.Abs(dot) / Math.Sqrt(aa * bb), 0, 1));
    }

    private static double? Triangles(Triangle a, Triangle b, Vec3d motion, double padding)
    {
        Span<Vec3d> ea = stackalloc Vec3d[] { a.B - a.A, a.C - a.B, a.A - a.C };
        Span<Vec3d> eb = stackalloc Vec3d[] { b.B - b.A, b.C - b.B, b.A - b.C };
        var na = Vec3d.Cross(ea[0], ea[1]);
        var nb = Vec3d.Cross(eb[0], eb[1]);
        Span<Vec3d> axes = stackalloc Vec3d[17];
        axes[0] = na; axes[1] = nb;
        var k = 2;
        foreach (var x in ea)
        foreach (var y in eb) axes[k++] = Vec3d.Cross(x, y);
        foreach (var edge in ea) axes[k++] = Vec3d.Cross(na, edge);
        foreach (var edge in eb) axes[k++] = Vec3d.Cross(nb, edge);
        double enter = 0, exit = 1;
        foreach (var axis in axes)
        {
            var length = axis.Length;
            if (length < 1e-12) continue;
            var a0 = Vec3d.Dot(a.A, axis); var a1 = Vec3d.Dot(a.B, axis); var a2 = Vec3d.Dot(a.C, axis);
            var b0 = Vec3d.Dot(b.A, axis); var b1 = Vec3d.Dot(b.B, axis); var b2 = Vec3d.Dot(b.C, axis);
            var minA = Math.Min(a0, Math.Min(a1, a2)); var maxA = Math.Max(a0, Math.Max(a1, a2));
            var minB = Math.Min(b0, Math.Min(b1, b2)); var maxB = Math.Max(b0, Math.Max(b1, b2));
            var speed = Vec3d.Dot(motion, axis);
            var margin = padding * length;
            if (Math.Abs(speed) < 1e-14)
            {
                if (maxA + margin < minB || minA - margin > maxB) return null;
                continue;
            }
            var near = (minB - maxA - margin) / speed;
            var far = (maxB - minA + margin) / speed;
            if (near > far) (near, far) = (far, near);
            enter = Math.Max(enter, near); exit = Math.Min(exit, far);
            if (enter > exit) return null;
        }
        return exit > 1e-9 ? enter : null;
    }
}
