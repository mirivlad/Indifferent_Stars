using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public sealed class TacticalContact
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required Vec3d Position { get; init; }
    public required Color Color { get; init; }
    public bool IsPlayer { get; init; }
}

/// <summary>
/// Playable top-down 2D navigation view. It is drawn from the same server snapshots
/// as the 3D views; the third axis is exposed as relative altitude markers.
/// </summary>
public sealed partial class TacticalView : Control
{
    private static readonly Color Background = new(0.004f, 0.008f, 0.016f, 1f);
    private static readonly Color GridColor = new(0.25f, 0.45f, 0.6f, 0.10f);
    private static readonly Color RingColor = new(0.3f, 0.55f, 0.72f, 0.22f);
    private static readonly Color TextDim = new(0.52f, 0.64f, 0.74f, 0.85f);
    private static readonly Color PlayerColor = new(0.55f, 0.88f, 1.0f);

    public WorldCatalog? Catalog { get; set; }
    public Vec3d PlayerPosition { get; set; }
    public Vec3d PlayerVelocity { get; set; }
    public float PlayerYaw { get; set; }
    public float PlayerAltitude { get; set; }
    public uint PlayerId { get; set; }
    public uint TargetId { get; set; }
    public bool HasTarget { get; set; }
    public uint ApproachTargetId { get; set; }
    public double ZoomRange { get; private set; } = 8500;
    private double _targetZoom = 8500;
    private Vec3d _center = Vec3d.Zero;
    private Vec3d _targetCenter = Vec3d.Zero;
    private bool _centerInitialized;
    public List<TacticalContact> Contacts { get; } = new();

    private readonly List<LabelItem> _labels = new(64);
    private readonly List<Rect2> _placedLabels = new(64);

    private readonly record struct LabelItem(string Text, Vector2 Position, Color Color, int Size, bool Primary);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X < 8 || size.Y < 8)
            return;

        DrawRect(new Rect2(Vector2.Zero, size), Background);
        _labels.Clear();
        _placedLabels.Clear();
        var center = size * 0.5f;
        var scale = (float)(Mathf.Min(size.X, size.Y) * 0.43 / ZoomRange);

        DrawGrid(center, scale, size);
        DrawRangeRings(center, scale, size);
        DrawSectorBoundaries(center, scale, size);
        DrawAsteroids(center, scale, size);
        if (ApproachTargetId != 0 && Catalog?.Find((int)ApproachTargetId) is { } destination)
            DrawLine(Project(center, scale, PlayerPosition), Project(center, scale, destination.Position), new Color(0.55f, 0.9f, 0.8f, 0.8f), 1.5f);
        else if (ApproachTargetId != 0 && Contacts.Find(contact => contact.Id == ApproachTargetId) is { } movingDestination)
            DrawLine(Project(center, scale, PlayerPosition), Project(center, scale, movingDestination.Position), new Color(0.55f, 0.9f, 0.8f, 0.8f), 1.5f);
        DrawContactLabels(center, scale, size);
        DrawPlayer(center, scale);
        DrawPlanetNavigation();
        FlushLabels();
    }

    private void DrawPlanetNavigation()
    {
        if (Catalog is null) return;
        var navigation = SurfaceNavigation.Measure(Catalog.LocalFrameOrigin + PlayerPosition, PlayerVelocity, Catalog.Planet);
        var position = new Vector2(30, 190);
        DrawRect(new Rect2(position - new Vector2(8, 18), new Vector2(390, 95)), new Color(0.008f, 0.02f, 0.035f, 0.94f));
        DrawLabel(Catalog.Planet.Name, position, PlayerColor, 16, true);
        DrawLabel(Locale.Format("planet_surface_distance", navigation.DistanceMetres / 1000), position + new Vector2(0, 25), PlayerColor, 16, true);
        DrawLabel(Locale.Format("planet_closing_speed", navigation.ClosingMetresPerSecond), position + new Vector2(0, 50), TextDim, 14, true);
    }

    public override void _Process(double delta)
    {
        if (!_centerInitialized)
        {
            _center = _targetCenter = PlayerPosition;
            _centerInitialized = true;
        }
        var blend = 1 - Math.Exp(-10 * delta);
        ZoomRange += (_targetZoom - ZoomRange) * blend;
        _center = Vec3d.Lerp(_center, _targetCenter, blend);
    }

    public void ZoomAt(Vector2 screen, double factor)
    {
        var next = Math.Clamp(_targetZoom * factor, 300, 250_000_000_000.0);
        var pixels = Math.Min(Size.X, Size.Y) * 0.43;
        var offset = screen - Size * 0.5f;
        var difference = (_targetZoom - next) / pixels;
        _targetCenter += new Vec3d(offset.X * difference, 0, offset.Y * difference);
        _targetZoom = next;
    }

    public void Pan(Vector2 motion)
    {
        var metresPerPixel = _targetZoom / (Math.Min(Size.X, Size.Y) * 0.43);
        _targetCenter -= new Vec3d(motion.X * metresPerPixel, 0, motion.Y * metresPerPixel);
    }

    public void CenterOnPlayer() => _targetCenter = PlayerPosition;

    public void ShowSystem()
    {
        if (Catalog is null) return;
        _targetCenter = Catalog.Star.SystemPosition - Catalog.LocalFrameOrigin;
        _targetZoom = 200_000_000_000;
    }

    public Vector2 ScreenPosition(Vec3d world) =>
        Project(Size * 0.5f, (float)(Math.Min(Size.X, Size.Y) * 0.43 / ZoomRange), world);

    private void DrawGrid(Vector2 center, float scale, Vector2 size)
    {
        var step = Math.Pow(10, Math.Floor(Math.Log10(ZoomRange / 4)));
        var pixels = step * scale;
        if (pixels < 12)
            return;

        var halfWidth = size.X * 0.5f;
        var halfHeight = size.Y * 0.5f;
        var systemOrigin = Catalog?.LocalFrameOrigin ?? Vec3d.Zero;
        var startX = Math.Floor((_center.X + systemOrigin.X) / step) * step - systemOrigin.X;
        var startZ = Math.Floor((_center.Z + systemOrigin.Z) / step) * step - systemOrigin.Z;

        for (var i = -20; i <= 20; i++)
        {
            var wx = (float)((startX + i * step - _center.X) * scale);
            if (Mathf.Abs(wx) <= halfWidth)
                DrawLine(new Vector2(center.X + wx, 0), new Vector2(center.X + wx, size.Y), GridColor, 1);
            var wz = (float)((startZ + i * step - _center.Z) * scale);
            if (Mathf.Abs(wz) <= halfHeight)
                DrawLine(new Vector2(0, center.Y + wz), new Vector2(size.X, center.Y + wz), GridColor, 1);
        }
    }

    private void DrawRangeRings(Vector2 center, float scale, Vector2 size)
    {
        center = Project(center, scale, PlayerPosition);
        var rings = new[] { 2000f, 5000f, 10000f, 20000f };
        foreach (var ring in rings)
        {
            var radius = ring * scale;
            if (radius < 24 || radius > Mathf.Max(size.X, size.Y) * 0.75f)
                continue;
            DrawArc(center, radius, 0, Mathf.Tau, 96, RingColor, 1.0f);
            DrawLabel($"{ring / 1000:F0} km", center + new Vector2(radius * 0.7f - 16, -radius * 0.7f - 14), TextDim, 11);
        }
    }

    private void DrawSectorBoundaries(Vector2 center, float scale, Vector2 size)
    {
        if (Catalog is null || ZoomRange >= 1_000_000)
            return;

        foreach (var sector in Catalog.Sectors)
        {
            var screen = Project(center, scale, sector.Center);
            var radius = (float)(sector.Radius * scale);
            if (radius < 30 || radius > Mathf.Max(size.X, size.Y))
                continue;
            if (screen.X + radius < 0 || screen.X - radius > size.X ||
                screen.Y + radius < 0 || screen.Y - radius > size.Y)
                continue;

            var color = new Color(0.35f, 0.62f, 0.82f, 0.16f);
            DrawArc(screen, radius, 0, Mathf.Tau, 96, color, 1.0f);
            DrawLabel(sector.Name, screen + new Vector2(-radius, -radius - 14), TextDim, 10);
        }
    }

    private void DrawAsteroids(Vector2 center, float scale, Vector2 size)
    {
        if (Catalog is null)
            return;

        if (ZoomRange >= 100_000_000)
        {
            DrawSystemBodies(center, scale, size);
            return;
        }

        var margin = 40f;
        foreach (var asteroid in ZoomRange < 1_000_000 ? Catalog.Asteroids : Array.Empty<StaticBody>())
        {
            var screen = Project(center, scale, asteroid.Position);
            if (screen.X < -margin || screen.X > size.X + margin || screen.Y < -margin || screen.Y > size.Y + margin)
                continue;
            var color = new Color(0.62f, 0.58f, 0.52f, 0.5f);
            var radius = Mathf.Clamp((float)asteroid.Radius * scale, 0.8f, 4f);
            DrawCircle(screen, radius, color);
        }

        if (Catalog is not null)
        {
            DrawBody(Catalog.Station, center, scale, new Color(0.45f, 0.92f, 1.0f), Locale.Text("type_station"));
            DrawCelestialBearings(center, scale, size);
        }
    }

    /// <summary>
    /// Celestial bodies are millions of kilometres away, so they are not drawn on the
    /// local plot. Their true bearing is shown as an edge marker with the real distance,
    /// including the vertical axis that the 2D plot compresses.
    /// </summary>
    private void DrawCelestialBearings(Vector2 center, float scale, Vector2 size)
    {
        if (Catalog is null)
            return;

        var player = PlayerPosition;
        center = Project(center, scale, player);
        DrawCelestial(Catalog.Planet, player, center, size, new Color(0.92f, 0.80f, 0.55f));
        DrawCelestial(Catalog.Star, player, center, size, new Color(1.0f, 0.72f, 0.36f));
    }

    private void DrawSystemBodies(Vector2 center, float scale, Vector2 size)
    {
        if (Catalog is null) return;
        var starPosition = Catalog.Star.SystemPosition - Catalog.LocalFrameOrigin;
        var planetPosition = Catalog.Planet.SystemPosition - Catalog.LocalFrameOrigin;
        var starScreen = Project(center, scale, starPosition);
        var orbitRadius = (float)(Vec3d.Distance(Catalog.Star.SystemPosition, Catalog.Planet.SystemPosition) * scale);
        if (orbitRadius > 2 && orbitRadius < 10000)
            DrawArc(starScreen, orbitRadius, 0, Mathf.Tau, 256, RingColor, 1);
        DrawSystemBody(Catalog.Star, starPosition, center, scale, size, new Color(1, 0.72f, 0.36f));
        DrawSystemBody(Catalog.Planet, planetPosition, center, scale, size, new Color(0.92f, 0.8f, 0.55f));
    }

    private void DrawSystemBody(CelestialBody body, Vec3d position, Vector2 center, float scale, Vector2 size, Color color)
    {
        var screen = Project(center, scale, position);
        if (screen.X < -50 || screen.X > size.X + 50 || screen.Y < -50 || screen.Y > size.Y + 50) return;
        var radius = Mathf.Clamp((float)(body.Radius * scale), 5, 40);
        DrawCircle(screen, radius, color);
        DrawLabel(body.Name, screen + new Vector2(radius + 6, -5), color, 13, true);
    }

    private void DrawCelestial(CelestialBody body, Vec3d player, Vector2 center, Vector2 size, Color color)
    {
        var offset = body.SystemPosition - (Catalog!.LocalFrameOrigin + player);
        var distance = Math.Max(0, offset.Length - body.Radius);
        var direction = offset.Normalized();
        var horizontal = new Vector2((float)direction.X, (float)direction.Z);
        var radius = Mathf.Min(size.X, size.Y) * 0.42f;

        Vector2 marker;
        if (Mathf.Abs(direction.Y) > 0.96f)
        {
            marker = direction.Y < 0
                ? new Vector2(center.X, center.Y + radius * 0.88f)
                : new Vector2(center.X, center.Y - radius * 0.88f);
        }
        else
        {
            var dir = horizontal.LengthSquared() > 0.0001f ? horizontal.Normalized() : new Vector2(0, -1);
            marker = center + dir * radius;
        }

        marker = new Vector2(
            Mathf.Clamp(marker.X, 40, size.X - 40),
            Mathf.Clamp(marker.Y, 80, size.Y - 80));

        DrawCircle(marker, 5.0f, new Color(color.R, color.G, color.B, 0.85f));
        DrawArc(marker, 11.0f, 0, Mathf.Tau, 24, new Color(color.R, color.G, color.B, 0.45f), 1.0f);

        var vertical = direction.Y < -0.05f ? " " + Locale.Text("below") : direction.Y > 0.05f ? " " + Locale.Text("above") : "";
        var kind = body.Kind == CelestialKind.Star ? Locale.Text("type_star") : Locale.Text("type_planet");
        var labelX = marker.X + 14;
        if (labelX > size.X - 220)
            labelX = marker.X - 214;
        DrawLabel(kind + " " + body.Name, new Vector2(labelX, marker.Y - 14),
            new Color(color.R, color.G, color.B, 0.95f), 12, true);
        DrawLabel($"{FormatDistance(distance)}{vertical}", new Vector2(labelX, marker.Y + 1), TextDim, 10);
    }

    private static string FormatDistance(double metres)
    {
        if (metres >= 1_000_000_000) return $"{metres / 149_597_870_700.0:F2} AU";
        if (metres >= 100_000) return $"{metres / 1000.0:N0} km";
        return $"{metres / 1000.0:F1} km";
    }

    private void DrawBody(StaticBody body, Vector2 center, float scale, Color color, string kind)
    {
        var screen = Project(center, scale, body.Position);
        var viewSize = Size;
        var margin = 160f;
        if (screen.X < -margin || screen.X > viewSize.X + margin || screen.Y < -margin || screen.Y > viewSize.Y + margin)
        {
            if (body.Id == (int)TargetId)
                DrawOffscreenArrow(screen, center, color, body.Name);
            return;
        }

        var radius = Mathf.Clamp((float)body.Radius * scale, 4f, 40f);
        DrawCircle(screen, radius, new Color(color.R, color.G, color.B, 0.22f));
        DrawArc(screen, radius, 0, Mathf.Tau, 32, color, 1.5f);
        DrawCircle(screen, 2.5f, color);
        var distance = Vec3d.Distance(PlayerPosition, body.Position) / 1000f;
        DrawLabel(body.Name, screen + new Vector2(radius + 7, -8), new Color(color.R, color.G, color.B, 0.95f), 13, true);
        DrawLabel($"{kind} · {distance:F1} km", screen + new Vector2(radius + 7, 7), TextDim, 10);
        DrawAltitudeTick(screen, (float)(PlayerPosition.Y - body.Position.Y), color);
    }

    private void DrawContactLabels(Vector2 center, float scale, Vector2 size)
    {
        if (ZoomRange >= 1_000_000)
            return;
        foreach (var contact in Contacts)
        {
            var screen = Project(center, scale, contact.Position);
            var margin = 80f;
            if (screen.X < -margin || screen.X > size.X + margin || screen.Y < -margin || screen.Y > size.Y + margin)
                continue;

            var isTarget = HasTarget && contact.Id == TargetId;
            if (contact.IsPlayer && contact.Id == PlayerId)
                continue;

            var color = contact.IsPlayer ? new Color(0.55f, 0.95f, 0.7f) : contact.Color;
            if (isTarget)
            {
                DrawArc(screen, 14, 0, Mathf.Tau, 24, new Color(0.45f, 1.0f, 0.65f), 1.5f);
                DrawLine(center, screen, new Color(0.45f, 1.0f, 0.65f, 0.22f), 1.0f);
            }

            DrawColoredPolygon(new[]
            {
                screen + new Vector2(0, -7),
                screen + new Vector2(6, 6),
                screen + new Vector2(-6, 6),
            }, color);

            DrawLabel(contact.Name, screen + new Vector2(10, -8), color, 13, true);
            var distance = Vec3d.Distance(PlayerPosition, contact.Position) / 1000f;
            DrawLabel($"{Locale.Text("type_" + contact.Kind.ToLowerInvariant())} · {distance:F2} km", screen + new Vector2(10, 7), TextDim, 10);
            DrawAltitudeTick(screen, (float)(PlayerPosition.Y - contact.Position.Y), color);
        }
    }

    private void DrawPlayer(Vector2 center, float scale)
    {
        center = Project(center, scale, PlayerPosition);
        if (ZoomRange >= 100_000_000)
        {
            DrawCircle(center, 4, PlayerColor);
            DrawLabel(Locale.Text("you"), center + new Vector2(8, -6), PlayerColor, 11);
            return;
        }
        var forward = new Vector2(-Mathf.Sin(PlayerYaw), -Mathf.Cos(PlayerYaw));
        var right = new Vector2(Mathf.Cos(PlayerYaw), -Mathf.Sin(PlayerYaw));
        var nose = center + forward * 20;
        var left = center - forward * 10 - right * 9;
        var rightWing = center - forward * 10 + right * 9;
        var tail = center - forward * 4;

        DrawColoredPolygon(new[] { nose, left, tail, rightWing }, new Color(PlayerColor.R, PlayerColor.G, PlayerColor.B, 0.95f));
        DrawLine(center, nose, new Color(1, 1, 1, 0.85f), 1.2f);
        DrawArc(center, 27, 0, Mathf.Tau, 32, new Color(PlayerColor.R, PlayerColor.G, PlayerColor.B, 0.4f), 1.0f);
        DrawLine(center + right * 30, center + right * 40, new Color(PlayerColor.R, PlayerColor.G, PlayerColor.B, 0.3f), 1.0f);
        DrawLine(center - right * 30, center - right * 40, new Color(PlayerColor.R, PlayerColor.G, PlayerColor.B, 0.3f), 1.0f);

        DrawLabel(Locale.Text("you"), center + new Vector2(0, 26), PlayerColor, 12);
        DrawLabel($"{Locale.Text("altitude")} {(PlayerAltitude >= 0 ? "+" : "")}{PlayerAltitude:F0} m", center + new Vector2(0, 40), TextDim, 11);
    }

    private void DrawAltitudeTick(Vector2 screen, float relativeAltitude, Color color)
    {
        if (Mathf.Abs(relativeAltitude) < 40)
            return;
        var direction = relativeAltitude > 0 ? -1f : 1f;
        var length = Mathf.Clamp(Mathf.Abs(relativeAltitude) / 120f, 4f, 26f);
        var band = new Color(0.45f, 0.9f, 1.0f, 0.55f);
        var below = new Color(1.0f, 0.65f, 0.35f, 0.55f);
        DrawLine(screen + new Vector2(-12, 0), screen + new Vector2(-12, direction * length),
            relativeAltitude > 0 ? band : below, 1.5f);
        DrawCircle(screen + new Vector2(-12, direction * length), 1.8f, relativeAltitude > 0 ? band : below);
    }

    private void DrawOffscreenArrow(Vector2 screen, Vector2 center, Color color, string label)
    {
        var direction = (screen - center).Normalized();
        var edge = center + direction * Mathf.Min(Size.X, Size.Y) * 0.42f;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        DrawColoredPolygon(new[]
        {
            edge + direction * 12,
            edge - direction * 4 + perpendicular * 7,
            edge - direction * 4 - perpendicular * 7,
        }, color);
        DrawLabel(label, edge + perpendicular * 14 + new Vector2(-20, -8), color, 11);
    }

    private Vector2 Project(Vector2 center, float scale, Vec3d world) =>
        center + new Vector2((float)((world.X - _center.X) * scale), (float)((world.Z - _center.Z) * scale));

    private void DrawLabel(string text, Vector2 position, Color color, int fontSize, bool primary = false)
    {
        _labels.Add(new LabelItem(text, position, color, fontSize, primary));
    }

    /// <summary>
    /// Places labels greedily and pushes overlapping secondary labels down (or up near
    /// the bottom edge) so dense clusters around the station stay readable.
    /// </summary>
    private void FlushLabels()
    {
        var font = ThemeDB.FallbackFont;
        foreach (var label in _labels.OrderByDescending(l => l.Primary).ThenByDescending(l => l.Size))
        {
            var textSize = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.Size);
            var rect = new Rect2(label.Position - new Vector2(2, 12), textSize + new Vector2(6, 5));
            var placed = rect;
            var step = textSize.Y + 4;
            var down = true;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var overlap = false;
                foreach (var other in _placedLabels)
                {
                    if (other.Intersects(placed))
                    {
                        overlap = true;
                        break;
                    }
                }

                if (!overlap)
                    break;

                if (down && placed.Position.Y + step + placed.Size.Y > Size.Y - 8)
                    down = false;
                placed.Position += new Vector2(0, down ? step : -step);
            }

            if (placed.Position.Y < 6 || placed.Position.Y + placed.Size.Y > Size.Y - 6)
                placed.Position = rect.Position;

            _placedLabels.Add(placed);

            var background = new Color(0.01f, 0.02f, 0.035f, label.Primary ? 0.55f : 0.4f);
            DrawRect(placed, background);
            var textPosition = new Vector2(placed.Position.X + 2, placed.Position.Y + placed.Size.Y - 5);
            DrawString(font, textPosition + new Vector2(1, 1), label.Text, HorizontalAlignment.Left, -1, label.Size,
                new Color(0, 0, 0, 0.85f));
            DrawString(font, textPosition, label.Text, HorizontalAlignment.Left, -1, label.Size, label.Color);
        }
    }

}
