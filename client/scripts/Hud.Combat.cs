using Godot;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public sealed partial class Hud
{
    private Panel _combatPanel = null!;
    private Label _combatResources = null!;
    private Label _combatWeapon = null!;
    private Label _combatTarget = null!;
    private Label _combatWarning = null!;

    private void BuildCombat()
    {
        _combatPanel = CreatePanel();
        _combatPanel.Visible = false;
        Place(_combatPanel, 0, 0, new Vector2(24, 90), new Vector2(362, 80));
        _root.AddChild(_combatPanel);
        _combatResources = MakeLabel("", 12, Cyan);
        _combatWeapon = MakeLabel("", 12, Warm);
        _combatTarget = MakeLabel("", 12, Dim);
        _combatWarning = MakeLabel("", 12, Warm);
        Place(_combatResources, 0, 0, new Vector2(8, 4), new Vector2(346, 17));
        Place(_combatWeapon, 0, 0, new Vector2(8, 22), new Vector2(346, 17));
        Place(_combatTarget, 0, 0, new Vector2(8, 40), new Vector2(346, 17));
        Place(_combatWarning, 0, 0, new Vector2(8, 58), new Vector2(346, 17));
        _combatPanel.AddChild(_combatResources);
        _combatPanel.AddChild(_combatWeapon);
        _combatPanel.AddChild(_combatTarget);
        _combatPanel.AddChild(_combatWarning);
    }

    public void SetCombat(CombatStatusMessage? status, HullDefinition? hull, double? range, double facing,
        bool docked, string recoverKey, string repairKey = "")
    {
        _combatPanel.Visible = status is not null;
        if (status is null) return;
        _combatResources.Text = Locale.Format("combat_resources", status.Shield, status.Armor, status.Hull, status.Energy);
        if (status.Destroyed)
        {
            _combatWeapon.Text = Locale.Text("combat_destroyed");
            _combatTarget.Text = status.RecoveryIn > 0 ? Locale.Format("combat_wait", status.RecoveryIn) :
                Locale.Format("combat_recover_prompt", recoverKey);
            _combatWarning.Text = "";
            return;
        }
        var guidance = "";
        if (status.Slots.Length == 0)
            _combatWeapon.Text = Locale.Text("combat_unarmed");
        else
        {
            var slot = status.Slots[status.SelectedSlot];
            var label = Locale.Text("weapon_" + (int)slot.Kind);
            var weapon = WeaponCatalog.Get(slot.Kind);
            var readiness = status.Weapons <= .2f ? Locale.Text("combat_disabled") :
                weapon.Magazine > 0 && slot.Loaded == 0 && slot.Reserve == 0 ? Locale.Text("combat_empty") :
                slot.ReadyIn > .03 ? Locale.Format("combat_reload", slot.ReadyIn) :
                status.Energy * (hull?.ReactorOutput ?? 0) < weapon.Energy ? Locale.Text("combat_low_energy") : Locale.Text("ready");
            _combatWeapon.Text = weapon.Magazine == 0
                ? $"{label} · {readiness}" : $"{label} · {slot.Loaded}/{slot.Reserve} · {readiness}";
            if (range is { } distanceToTarget)
                guidance = Locale.Text(distanceToTarget > weapon.RangeMetres ? "combat_out_of_range" :
                    facing < System.Math.Cos(weapon.AimConeRadians) ? "combat_align" : "combat_aimed");
        }
        _combatTarget.Text = docked && (status.Hull < 1 || status.Armor < 1)
            ? Locale.Format("combat_repair_prompt", repairKey)
            : range is { } distance
                ? Locale.Format("combat_target_range", distance / 1000, Locale.Text("combat_order_" + (int)status.Order))
                : Locale.Text("combat_no_lock");
        _combatWarning.Text = status.Engines < .65f || status.Sensors < .65f || status.Weapons < .65f
            ? Locale.Format("combat_subsystems", status.Engines, status.Sensors, status.Weapons) : guidance;
    }
}
