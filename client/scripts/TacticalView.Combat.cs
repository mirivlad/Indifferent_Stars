using System;
using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class TacticalView
{
    private const float TraceLifetime = .62f;
    private readonly List<(CombatEventMessage Event, float Age, float Intensity)> _combatTraces = new();
    public WreckContact[] Wrecks { get; set; } = Array.Empty<WreckContact>();

    public void AddCombatEvent(CombatEventMessage effect, float intensity)
    {
        if (intensity <= 0) return;
        if (_combatTraces.Count == 48) _combatTraces.RemoveAt(0);
        _combatTraces.Add((effect, 0, intensity));
    }

    public void AdvanceCombat(float dt)
    {
        for (var i = _combatTraces.Count - 1; i >= 0; i--)
        {
            var trace = _combatTraces[i];
            trace.Age += dt;
            if (trace.Age >= TraceLifetime) _combatTraces.RemoveAt(i);
            else _combatTraces[i] = trace;
        }
    }

    private void DrawCombat(Vector2 center, float scale, Vector2 size)
    {
        if (ZoomRange > 250_000) return;
        var rect = new Rect2(Vector2.Zero, size);
        foreach (var wreck in Wrecks)
        {
            var position = Project(center, scale, wreck.Position);
            if (!rect.HasPoint(position)) continue;
            var color = new Color(.69f, .58f, .49f);
            DrawArc(position, 5, 0, Mathf.Tau, 4, color, 1.6f, true);
            DrawLabel(Locale.Text("combat_wreck"), position + new Vector2(9, 4), color, 13);
        }

        foreach (var (effect, age, intensity) in _combatTraces)
        {
            var end = Project(center, scale, effect.To);
            var alpha = intensity * (1 - age / TraceLifetime);
            if (effect.Kind is CombatEventKind.Fired or CombatEventKind.MissileTrail)
            {
                var start = Project(center, scale, effect.From);
                if (!ClipLine(ref start, ref end, size)) continue;
                var color = CombatEffects.WeaponColor(effect.Weapon);
                DrawLine(start, end, new Color(color, alpha), effect.Weapon == WeaponKind.BeamLaser ? 3 : 1.6f, true);
            }
            else if (rect.HasPoint(end))
            {
                var color = effect.Kind == CombatEventKind.ShieldHit ? new Color(.23f, .76f, 1, alpha) : new Color(1, .42f, .2f, alpha);
                var radius = effect.Kind == CombatEventKind.Destroyed ? 9 : 4;
                DrawArc(end, radius + age * 19, 0, Mathf.Tau, 24, color, 2, true);
            }
        }
    }
}
