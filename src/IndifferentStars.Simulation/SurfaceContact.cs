using System;
using System.Buffers;
using System.Numerics;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Simulation;

public readonly record struct SurfaceHit(double Time, Vec3d Point, Vec3d Normal);

public static class SurfaceContact
{
    // Rotating surfaces are bounded to at most 5 cm deviation from each midpoint pose.
    public const double RotationTolerance = 0.05;

    public static double? Sweep(SurfaceMesh a, double scaleA, Vec3d startA, Vec3d endA, Quaternion fromA, Quaternion toA,
        SurfaceMesh b, double scaleB, Vec3d startB, Vec3d endB, Quaternion fromB, Quaternion toB)
        => SweepHit(a, scaleA, startA, endA, fromA, toA, b, scaleB, startB, endB, fromB, toB)?.Time;

    public static SurfaceHit? SweepHit(SurfaceMesh a, double scaleA, Vec3d startA, Vec3d endA, Quaternion fromA, Quaternion toA,
        SurfaceMesh b, double scaleB, Vec3d startB, Vec3d endB, Quaternion fromB, Quaternion toB, bool skipSeparating = false)
    {
        var angularA = skipSeparating ? AngularMotion(fromA, toA) : Vec3d.Zero;
        var angularB = skipSeparating ? AngularMotion(fromB, toB) : Vec3d.Zero;
        var relativeStart = startA - startB;
        var relativeEnd = endA - endB;
        var radius = a.Radius * scaleA + b.Radius * scaleB;
        var motion = relativeEnd - relativeStart;
        var nearest = motion.LengthSquared > 0
            ? relativeStart + motion * Math.Clamp(-Vec3d.Dot(relativeStart, motion) / motion.LengthSquared, 0, 1) : relativeStart;
        if (nearest.Length > radius) return null;
        var arcA = Angle(fromA, toA) * a.Radius * scaleA;
        var arcB = Angle(fromB, toB) * b.Radius * scaleB;
        // A sphere containing the complete moving hull cannot reach this surface.
        // Include B's entire rotational travel and the narrow-phase safety margin.
        var localStart = SurfaceMesh.Rotate(relativeStart, Quaternion.Conjugate(fromB)) / scaleB;
        var reach = a.Radius * scaleA + motion.Length + arcB + RotationTolerance + 1e-6;
        if (!b.Root.Bounds.Expand(reach / scaleB).Intersects(new Bounds(localStart, localStart))) return null;
        if (SurfaceQuery.Distance(b, localStart) * scaleB > reach) return null;
        var slices = Math.Max(1, (int)Math.Ceiling((arcA + arcB) / (2 * RotationTolerance)));
        var cachedBounds = ArrayPool<Bounds>.Shared.Rent(a.NodeCount);
        var boundsStamp = ArrayPool<int>.Shared.Rent(a.NodeCount);
        var cachedTriangles = ArrayPool<Triangle>.Shared.Rent(a.Triangles.Length);
        var triangleStamp = ArrayPool<int>.Shared.Rent(a.Triangles.Length);
        Array.Clear(boundsStamp, 0, a.NodeCount);
        Array.Clear(triangleStamp, 0, a.Triangles.Length);
        try
        {
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
                SurfaceHit? firstHit = null;
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
                    return result.Expand(padding + 1e-6);
                }
                // Each pose is invariant for this slice, even when BVH pairs revisit A.
                Bounds NodeBounds(SurfaceMesh.Node node)
                {
                    if (boundsStamp[node.Index] != slice + 1)
                    {
                        cachedBounds[node.Index] = TransformBounds(node.Bounds);
                        boundsStamp[node.Index] = slice + 1;
                    }
                    return cachedBounds[node.Index];
                }
                Triangle TransformedTriangle(int index)
                {
                    if (triangleStamp[index] != slice + 1)
                    {
                        var source = a.Triangles[index];
                        cachedTriangles[index] = new Triangle(Transform(source.A), Transform(source.B), Transform(source.C));
                        triangleStamp[index] = slice + 1;
                    }
                    return cachedTriangles[index];
                }
                void Visit(SurfaceMesh.Node nodeA, Bounds boundsA, SurfaceMesh.Node nodeB, (double Enter, double Exit)? interval)
                {
                    if (first <= 1e-6 || interval is null || first is { } known && interval.Value.Enter > known) return;
                    if (nodeB.Left is null)
                    {
                        // A leaf can span gaps between long triangles. Reject the gap
                        // before descending the other mesh, not just at triangle pairs.
                        var reachable = false;
                        for (var ib = nodeB.Start; ib < nodeB.Start + nodeB.Count; ib++)
                        {
                            var candidate = BoxInterval(boundsA, b.TriangleBounds[ib], delta);
                            if (candidate is not null && (first is null || candidate.Value.Enter <= first)) { reachable = true; break; }
                        }
                        if (!reachable) return;
                    }
                    // Split by spatial extent, not the number of triangles in an asset.
                    var splitA = nodeA.Left is not null;
                    if (splitA && nodeB.Left is not null)
                        splitA = Area(boundsA.Max - boundsA.Min) >= Area(nodeB.Bounds.Max - nodeB.Bounds.Min);
                    if (splitA)
                    {
                        var leftBounds = NodeBounds(nodeA.Left!);
                        var rightBounds = NodeBounds(nodeA.Right!);
                        var left = BoxInterval(leftBounds, nodeB.Bounds, delta);
                        var right = BoxInterval(rightBounds, nodeB.Bounds, delta);
                        if (right is { } r && (left is null || r.Enter < left.Value.Enter))
                        {
                            Visit(nodeA.Right!, rightBounds, nodeB, right);
                            Visit(nodeA.Left!, leftBounds, nodeB, left);
                        }
                        else
                        {
                            Visit(nodeA.Left!, leftBounds, nodeB, left);
                            Visit(nodeA.Right!, rightBounds, nodeB, right);
                        }
                        return;
                    }
                    if (nodeB.Left is not null)
                    {
                        var left = BoxInterval(boundsA, nodeB.Left.Bounds, delta);
                        var right = BoxInterval(boundsA, nodeB.Right!.Bounds, delta);
                        if (right is { } r && (left is null || r.Enter < left.Value.Enter))
                        {
                            Visit(nodeA, boundsA, nodeB.Right!, right);
                            Visit(nodeA, boundsA, nodeB.Left, left);
                        }
                        else
                        {
                            Visit(nodeA, boundsA, nodeB.Left, left);
                            Visit(nodeA, boundsA, nodeB.Right!, right);
                        }
                        return;
                    }
                    for (var ia = nodeA.Start; ia < nodeA.Start + nodeA.Count; ia++)
                    {
                        var triangle = TransformedTriangle(ia);
                        var triangleBounds = triangle.Bounds.Expand(padding + 1e-6);
                        for (var ib = nodeB.Start; ib < nodeB.Start + nodeB.Count; ib++)
                        {
                            var candidate = BoxInterval(triangleBounds, b.TriangleBounds[ib], delta);
                            if (candidate is null || first is { } earlier && candidate.Value.Enter > earlier) continue;
                            var hit = TriangleContact(triangle, b.Triangles[ib], delta, padding,
                                candidate.Value.Enter, Math.Min(candidate.Value.Exit, first ?? 1));
                            if (hit is not { } contact || first is not null && contact.Time >= first) continue;
                            var shift = delta * contact.Time;
                            var moved = new Triangle(triangle.A + shift, triangle.B + shift, triangle.C + shift);
                            var point = ContactPoint(moved, b.Triangles[ib]);
                            var normal = contact.Normal;
                            var time = (slice + contact.Time) / slices;
                            var centerB = Vec3d.Lerp(startB, endB, time);
                            var worldPoint = centerB + SurfaceMesh.Rotate(point * scaleB, Quaternion.Conjugate(inverseB));
                            var worldNormal = SurfaceMesh.Rotate(normal, Quaternion.Conjugate(inverseB)).Normalized();
                            if (skipSeparating && time <= 1e-6)
                            {
                                var centerA = Vec3d.Lerp(startA, endA, time);
                                var velocityA = endA - startA + Vec3d.Cross(angularA, worldPoint - centerA);
                                var velocityB = endB - startB + Vec3d.Cross(angularB, worldPoint - centerB);
                                if (Vec3d.Dot(velocityA - velocityB, worldNormal) >= -1e-9) continue;
                            }
                            first = contact.Time;
                            firstHit = new SurfaceHit(time, worldPoint, worldNormal);
                        }
                    }
                }
                var rootBounds = NodeBounds(a.Root);
                Visit(a.Root, rootBounds, b.Root, BoxInterval(rootBounds, b.Root.Bounds, delta));
                if (firstHit is not null) return firstHit;
            }
            return null;
        }
        finally
        {
            ArrayPool<Bounds>.Shared.Return(cachedBounds);
            ArrayPool<int>.Shared.Return(boundsStamp);
            ArrayPool<Triangle>.Shared.Return(cachedTriangles);
            ArrayPool<int>.Shared.Return(triangleStamp);
        }
    }

    private static (double Enter, double Exit)? BoxInterval(Bounds a, Bounds b, Vec3d motion)
    {
        double enter = 0, exit = 1;
        if (!Slab(a.Min.X, a.Max.X, b.Min.X, b.Max.X, motion.X, ref enter, ref exit) ||
            !Slab(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y, motion.Y, ref enter, ref exit) ||
            !Slab(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z, motion.Z, ref enter, ref exit)) return null;
        return (enter, exit);
    }

    private static bool Slab(double minA, double maxA, double minB, double maxB, double speed, ref double enter, ref double exit)
    {
        if (Math.Abs(speed) < 1e-14) return maxA >= minB && minA <= maxB;
        var near = (minB - maxA) / speed;
        var far = (maxB - minA) / speed;
        if (near > far) (near, far) = (far, near);
        enter = Math.Max(enter, near); exit = Math.Min(exit, far);
        return enter <= exit;
    }

    private static double Area(Vec3d extent) =>
        extent.X * extent.Y + extent.Y * extent.Z + extent.Z * extent.X;

    private static double Angle(Quaternion a, Quaternion b)
    {
        if (a == b) return 0;
        var dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W;
        var aa = (double)a.X * a.X + (double)a.Y * a.Y + (double)a.Z * a.Z + (double)a.W * a.W;
        var bb = (double)b.X * b.X + (double)b.Y * b.Y + (double)b.Z * b.Z + (double)b.W * b.W;
        return 2 * Math.Acos(Math.Clamp(Math.Abs(dot) / Math.Sqrt(aa * bb), 0, 1));
    }

    private static double? Triangles(Triangle a, Triangle b, Vec3d motion, double padding, double enter, double exit)
        => TriangleContact(a, b, motion, padding, enter, exit)?.Time;

    private static SurfaceHit? TriangleContact(Triangle a, Triangle b, Vec3d motion, double padding, double enter, double exit)
    {
        Span<Vec3d> ea = stackalloc Vec3d[] { a.B - a.A, a.C - a.B, a.A - a.C };
        Span<Vec3d> eb = stackalloc Vec3d[] { b.B - b.A, b.C - b.B, b.A - b.C };
        var na = Vec3d.Cross(ea[0], ea[1]);
        var nb = Vec3d.Cross(eb[0], eb[1]);
        var sizeA = Math.Max(ea[0].LengthSquared, Math.Max(ea[1].LengthSquared, ea[2].LengthSquared));
        var sizeB = Math.Max(eb[0].LengthSquared, Math.Max(eb[1].LengthSquared, eb[2].LengthSquared));
        if (na.LengthSquared <= sizeA * sizeA * 1e-24 || nb.LengthSquared <= sizeB * sizeB * 1e-24) return null;
        Span<Vec3d> axes = stackalloc Vec3d[17];
        axes[0] = na; axes[1] = nb;
        var k = 2;
        foreach (var x in ea)
        foreach (var y in eb) axes[k++] = Vec3d.Cross(x, y);
        foreach (var edge in ea) axes[k++] = Vec3d.Cross(na, edge);
        foreach (var edge in eb) axes[k++] = Vec3d.Cross(nb, edge);
        var normal = Vec3d.Zero;
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
            if (near >= enter - 1e-10) normal = axis / length * (speed < 0 ? 1 : -1);
            enter = Math.Max(enter, near); exit = Math.Min(exit, far);
            if (enter > exit) return null;
        }
        if (exit <= 1e-9) return null;
        if (enter <= 1e-8 || normal.LengthSquared < .5) normal = nb.Normalized();
        return new SurfaceHit(enter, Vec3d.Zero, normal);
    }
    public static Vec3d AngularMotion(Quaternion from, Quaternion to)
    {
        var q = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (q.W < 0) q = new Quaternion(-q.X,-q.Y,-q.Z,-q.W);
        var sine = Math.Sqrt(Math.Max(0,1-(double)q.W*q.W));
        if (sine < 1e-7) return Vec3d.Zero;
        return new Vec3d(q.X,q.Y,q.Z) * (2*Math.Acos(Math.Clamp(q.W,-1,1))/sine);
    }

    private static Vec3d ContactPoint(Triangle a, Triangle b)
    {
        Span<Vec3d> av = stackalloc Vec3d[] {a.A,a.B,a.C};
        Span<Vec3d> bv = stackalloc Vec3d[] {b.A,b.B,b.C};
        var best = double.PositiveInfinity;
        var result = b.A;
        for (var i=0;i<3;i++)
        {
            var point = SurfaceQuery.Closest(b,av[i]);
            var distance = (point-av[i]).LengthSquared;
            if (distance<best) {best=distance;result=point;}
            point = SurfaceQuery.Closest(a,bv[i]);
            distance = (point-bv[i]).LengthSquared;
            if (distance<best) {best=distance;result=bv[i];}
            for (var j=0;j<3;j++)
            {
                var da=av[(i+1)%3]-av[i]; var db=bv[(j+1)%3]-bv[j]; var r=av[i]-bv[j];
                var aa=da.LengthSquared;var bb=db.LengthSquared;var ab=Vec3d.Dot(da,db);
                var ar=Vec3d.Dot(da,r);var br=Vec3d.Dot(db,r);var denominator=aa*bb-ab*ab;
                var u=denominator>1e-20?Math.Clamp((ab*br-bb*ar)/denominator,0,1):0;
                var v=bb>1e-20?Math.Clamp((ab*u+br)/bb,0,1):0;
                u=aa>1e-20?Math.Clamp((ab*v-ar)/aa,0,1):0;
                point=bv[j]+db*v;
                distance=(av[i]+da*u-point).LengthSquared;
                if(distance<best){best=distance;result=point;}
            }
        }
        return result;
    }

}
