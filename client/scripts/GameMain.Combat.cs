using Godot;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class GameMain
{
    private CombatStatusMessage? _combatStatus;
    private CombatEffects _combatEffects = null!;
    private ulong _wreckTick;
    private float _engineLeakDelay;

    private void ConnectCombat()
    {
        _net!.CombatStatusReceived += status =>
        {
            if ((_combatStatus?.Destroyed ?? false) != status.Destroyed)
            {
                UpdateCombatVisibility(status.Destroyed);
                if (status.Destroyed) _hud.ShowToast(Locale.Text("combat_destroyed"));
            }
            else if (status.Engines < .65f && (_combatStatus?.Engines ?? 1) >= .65f)
                _hud.ShowToast(Locale.Text("combat_engine_damage"));
            _combatStatus = status;
        };
        _net.WreckContactsReceived += report =>
        {
            if (report.Tick < _wreckTick) return;
            _wreckTick = report.Tick;
            _tactical.Wrecks = report.Contacts;
            _combatEffects.SetWrecks(report.Contacts);
        };
        _net.CombatReplied += reply =>
        {
            if (!reply.Accepted) _hud.ShowToast(Locale.Text("combat_unavailable"));
            else if (reply.Command == CombatCommand.Repair) _hud.ShowToast(Locale.Text("combat_repaired"));
        };
        _net.CombatEventReceived += effect =>
        {
            if (effect.Kind is CombatEventKind.HullHit or CombatEventKind.ShieldHit or CombatEventKind.Destroyed &&
                effect.TargetId == _playerId)
            {
                var strength = effect.Kind == CombatEventKind.Destroyed ? 1 : .42f;
                _impactShake = Mathf.Max(_impactShake, strength * _settings.CombatShake);
            }
            _combatEffects.Emit(effect, _renderOrigin, _settings.CombatEffects, _settings.EffectsVolume);
            _tactical.AddCombatEvent(effect, _settings.CombatEffects);
        };
    }

    private void UpdateCombatVisibility(bool destroyed)
    {
        if (_cockpit is not null) _cockpit.Visible = _mode == ViewMode.Cockpit && !destroyed;
        _playerShip?.SetExternalModelVisible(_mode != ViewMode.Cockpit && !destroyed);
    }

    private void UpdateCombat(float delta)
    {
        _combatEffects.Step(delta, _renderOrigin);
        _tactical.AdvanceCombat(delta);
        _engineLeakDelay -= delta;
        if (_engineLeakDelay <= 0 && _combatStatus is { Engines: < .8f, Destroyed: false } statusWithDamage &&
            _playerShip is { } player && !player.Track.Docked)
        {
            _engineLeakDelay = .18f + .65f * statusWithDamage.Engines;
            var exhaust = player.GlobalTransform * (Vector3.Back * (float)player.Hull.Radius);
            _combatEffects.EngineLeak(_renderOrigin + new Vec3d(exhaust.X, exhaust.Y, exhaust.Z),
                _renderOrigin, _settings.CombatEffects * (1 - statusWithDamage.Engines));
        }
        double? distance = null;
        var facing = 0.0;
        if (_combatStatus is { TargetId: > 0 } status && FindContact(status.TargetId) is { } contact)
        {
            distance = Vec3d.Distance(contact.Position, PlayerPosition());
            if (_playerShip is { } own)
                facing = ShipTrack.ToVector3(contact.Position - PlayerPosition()).Normalized().Dot(-own.GlobalBasis.Z);
        }
        _hud.SetCombat(_combatStatus, _playerShip?.Hull, distance, facing, _playerShip?.Track.Docked == true,
            _settings.Keys["combat_recover"].ToString(), _settings.Keys["combat_repair"].ToString());
    }

    private void SendCombatOrder(CombatCommand command)
    {
        if (_hasTarget && FindContact(_targetId) is { IsShip: true } && RequireLiveContact())
            _net?.RequestCombat(command, _targetId);
    }

    private void ToggleAttack()
    {
        if (_combatStatus is { Order: not CombatOrder.None }) _net?.RequestCombat(CombatCommand.Stop);
        else SendCombatOrder(CombatCommand.Attack);
    }

    private void CycleWeapon()
    {
        if (_combatStatus is { Slots.Length: > 1 } status)
            _net?.RequestCombat(CombatCommand.SelectWeapon, slot: (byte)((status.SelectedSlot + 1) % status.Slots.Length));
    }
}
