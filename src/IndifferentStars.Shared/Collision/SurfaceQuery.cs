namespace IndifferentStars.Shared.Collision;

public static class SurfaceQuery
{
    public static double Distance(SurfaceMesh mesh, Vec3d point)
    {
        var best = double.PositiveInfinity;
        void Visit(SurfaceMesh.Node node)
        {
            var min = node.Bounds.Min; var max = node.Bounds.Max;
            var closest = new Vec3d(Math.Clamp(point.X, min.X, max.X), Math.Clamp(point.Y, min.Y, max.Y), Math.Clamp(point.Z, min.Z, max.Z));
            if ((closest - point).LengthSquared >= best) return;
            if (node.Left is not null) { Visit(node.Left); Visit(node.Right!); return; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
                best = Math.Min(best, (Closest(mesh.Triangles[i], point) - point).LengthSquared);
        }
        Visit(mesh.Root);
        return Math.Sqrt(best);
    }

    public static double? Raycast(SurfaceMesh mesh, Vec3d from, Vec3d to)
    {
        var ray = to - from;
        double? first = null;
        var bounds = new Triangle(from, to, to).Bounds;
        void Visit(SurfaceMesh.Node node)
        {
            if (!bounds.Intersects(node.Bounds)) return;
            if (node.Left is not null) { Visit(node.Left); Visit(node.Right!); return; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var hit = Ray(mesh.Triangles[i], from, ray);
                if (hit is not null && (first is null || hit < first)) first = hit;
            }
        }
        Visit(mesh.Root);
        return first;
    }

    public static bool Contains(SurfaceMesh mesh, Vec3d point)
    {
        if (!mesh.Root.Bounds.Expand(1e-6).Intersects(new Bounds(point, point))) return false;
        var ray = new Vec3d(0.839173, 0.327913, 0.435817).Normalized() * (mesh.Radius * 4 + point.Length);
        var winding = 0;
        foreach (var triangle in mesh.Triangles)
        {
            if (Ray(triangle, point, ray) is not null)
                winding += Math.Sign(Vec3d.Dot(Vec3d.Cross(triangle.B - triangle.A, triangle.C - triangle.A), ray));
        }
        return winding != 0;
    }

    private static double? Ray(Triangle triangle, Vec3d from, Vec3d ray)
    {
        var ab = triangle.B - triangle.A; var ac = triangle.C - triangle.A;
        var h = Vec3d.Cross(ray, ac);
        var determinant = Vec3d.Dot(ab, h);
        if (Math.Abs(determinant) < 1e-12) return null;
        var s = from - triangle.A;
        var u = Vec3d.Dot(s, h) / determinant;
        if (u < -1e-9 || u > 1 + 1e-9) return null;
        var q = Vec3d.Cross(s, ab);
        var v = Vec3d.Dot(ray, q) / determinant;
        if (v < -1e-9 || u + v > 1 + 1e-9) return null;
        var t = Vec3d.Dot(ac, q) / determinant;
        return t >= 0 && t <= 1 ? t : null;
    }

    private static Vec3d Closest(Triangle t, Vec3d p)
    {
        var ab = t.B - t.A; var ac = t.C - t.A; var ap = p - t.A;
        var d1 = Vec3d.Dot(ab, ap); var d2 = Vec3d.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return t.A;
        var bp = p - t.B; var d3 = Vec3d.Dot(ab, bp); var d4 = Vec3d.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return t.B;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return t.A + ab * (d1 / (d1 - d3));
        var cp = p - t.C; var d5 = Vec3d.Dot(ab, cp); var d6 = Vec3d.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return t.C;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return t.A + ac * (d2 / (d2 - d6));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return t.B + (t.C - t.B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        var sum = va + vb + vc;
        if (Math.Abs(sum) < 1e-20) return t.A;
        return t.A + ab * (vb / sum) + ac * (vc / sum);
    }
}
