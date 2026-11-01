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
    public required Vector3 Position { get; init; }
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
    public Vector3 PlayerPosition { get; set; }
    public float PlayerYaw { get; set; }
    public float PlayerAltitude { get; set; }
    public uint PlayerId { get; set; }
    public uint TargetId { get; set; }
    public bool HasTarget { get; set; }
    public float ZoomRange { get; set; } = 8500f;
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
        var scale = Mathf.Min(size.X, size.Y) * 0.43f / ZoomRange;

        DrawGrid(center, scale, size);
        DrawRangeRings(center, scale, size);
        DrawAsteroids(center, scale, size);
        DrawContactLabels(center, scale, size);
        DrawPlayer(center, scale);
        DrawAltitudeGauge(size);
        FlushLabels();
    }

    private void DrawGrid(Vector2 center, float scale, Vector2 size)
    {
        var step = ZoomRange > 15000 ? 5000f : ZoomRange > 6500 ? 2000f : 1000f;
        var pixels = step * scale;
        if (pixels < 12)
            return;

        var halfWidth = size.X * 0.5f;
        var halfHeight = size.Y * 0.5f;
        var startX = Mathf.Floor(PlayerPosition.X / step) * step;
        var startZ = Mathf.Floor(PlayerPosition.Z / step) * step;

        for (var i = -20; i <= 20; i++)
        {
            var wx = (startX + i * step - PlayerPosition.X) * scale;
            if (Mathf.Abs(wx) <= halfWidth)
                DrawLine(new Vector2(center.X + wx, 0), new Vector2(center.X + wx, size.Y), GridColor, 1);
            var wz = (startZ + i * step - PlayerPosition.Z) * scale;
            if (Mathf.Abs(wz) <= halfHeight)
                DrawLine(new Vector2(0, center.Y + wz), new Vector2(size.X, center.Y + wz), GridColor, 1);
        }
    }

    private void DrawRangeRings(Vector2 center, float scale, Vector2 size)
    {
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

    private void DrawAsteroids(Vector2 center, float scale, Vector2 size)
    {
        if (Catalog is null)
            return;

        var margin = 40f;
        foreach (var asteroid in Catalog.Asteroids)
        {
            var screen = Project(center, scale, ToVector3(asteroid.Position));
            if (screen.X < -margin || screen.X > size.X + margin || screen.Y < -margin || screen.Y > size.Y + margin)
                continue;
            var color = new Color(0.62f, 0.58f, 0.52f, 0.5f);
            var radius = Mathf.Clamp((float)asteroid.Radius * scale, 0.8f, 4f);
            DrawCircle(screen, radius, color);
        }

        if (Catalog is not null)
        {
            DrawBody(Catalog.Station, center, scale, new Color(0.45f, 0.92f, 1.0f), "STATION");
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

        var player = ToVec(PlayerPosition);
        DrawCelestial(Catalog.Planet, player, center, size, new Color(0.92f, 0.80f, 0.55f));
        DrawCelestial(Catalog.Star, player, center, size, new Color(1.0f, 0.72f, 0.36f));
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

        var vertical = direction.Y < -0.05f ? " BELOW" : direction.Y > 0.05f ? " ABOVE" : "";
        var kind = body.Kind == CelestialKind.Star ? "STAR" : "PLANET";
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
        var screen = Project(center, scale, ToVector3(body.Position));
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
        var distance = Vec3d.Distance(ToVec(PlayerPosition), body.Position) / 1000f;
        DrawLabel(body.Name, screen + new Vector2(radius + 7, -8), new Color(color.R, color.G, color.B, 0.95f), 13, true);
        DrawLabel($"{kind} · {distance:F1} km", screen + new Vector2(radius + 7, 7), TextDim, 10);
        DrawAltitudeTick(screen, PlayerPosition.Y - (float)body.Position.Y, color);
    }

    private void DrawContactLabels(Vector2 center, float scale, Vector2 size)
    {
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
            var distance = Vec3d.Distance(ToVec(PlayerPosition), ToVec(contact.Position)) / 1000f;
            DrawLabel($"{contact.Kind} · {distance:F2} km", screen + new Vector2(10, 7), TextDim, 10);
            DrawAltitudeTick(screen, PlayerPosition.Y - contact.Position.Y, color);
        }
    }

    private void DrawPlayer(Vector2 center, float scale)
    {
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

        DrawLabel("YOU", center + new Vector2(0, 26), PlayerColor, 12);
        DrawLabel($"ALT {(PlayerAltitude >= 0 ? "+" : "")}{PlayerAltitude:F0} m", center + new Vector2(0, 40), TextDim, 11);
    }

    private void DrawAltitudeGauge(Vector2 size)
    {
        var x = size.X - 46;
        var centerY = size.Y * 0.47f;
        var half = 95f;
        DrawLine(new Vector2(x, centerY - half), new Vector2(x, centerY + half), new Color(0.4f, 0.65f, 0.8f, 0.4f), 1.0f);
        for (var i = -2; i <= 2; i++)
            DrawLine(new Vector2(x - 5, centerY + i * half / 2), new Vector2(x + 5, centerY + i * half / 2), TextDim, 1.0f);

        var normalized = Mathf.Clamp(PlayerAltitude / 4000f, -1f, 1f);
        var markerY = centerY - normalized * half;
        DrawColoredPolygon(new[]
        {
            new Vector2(x - 10, markerY),
            new Vector2(x - 3, markerY - 5),
            new Vector2(x - 3, markerY + 5),
        }, PlayerColor);
        DrawLabel("ALT", new Vector2(x - 14, centerY - half - 18), TextDim, 11);

        if (HasTarget)
        {
            var target = Contacts.Find(c => c.Id == TargetId);
            if (target is not null)
            {
                var targetAlt = target.Position.Y - PlayerPosition.Y;
                var targetMarker = centerY - Mathf.Clamp(targetAlt / 4000f, -1f, 1f) * half;
                DrawLine(new Vector2(x + 5, targetMarker), new Vector2(x + 12, targetMarker), new Color(0.45f, 1f, 0.65f), 2.0f);
            }
        }
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

    private Vector2 Project(Vector2 center, float scale, Vector3 world) =>
        center + new Vector2((world.X - PlayerPosition.X) * scale, (world.Z - PlayerPosition.Z) * scale);

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

    private static Vec3d ToVec(Vector3 position) => new(position.X, position.Y, position.Z);

    private static Vector3 ToVector3(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
}