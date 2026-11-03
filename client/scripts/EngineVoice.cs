using System;

namespace IndifferentStars.Client;

/// <summary>Continuous onboard engine sound; controls describe thrust, never ship speed.</summary>
public sealed class EngineVoice
{
    public const int SampleRate = 22050;
    private uint _random = 0x7a63b9d1;
    private double _thrustTarget, _maneuverTarget, _boostTarget, _cruiseTarget, _brakeTarget, _activeTarget;
    private double _thrust, _maneuver, _boost, _cruise, _brake, _active;
    private double _corePhase, _turbinePhase, _pulsePhase;
    private double _low, _body, _air, _side;
    private static readonly double LowFilter = Filter(65), BodyFilter = Filter(340), AirFilter = Filter(2400), SideFilter = Filter(950);
    private static readonly double Attack = FilterTime(.45), Release = FilterTime(1.1), Fast = FilterTime(.09), Slow = FilterTime(.7);

    public void SetControls(float throttle, float maneuver, bool boost, bool brake, bool cruise, bool active = true)
    {
        _activeTarget = active ? 1 : 0;
        _thrustTarget = active ? Math.Clamp(Math.Abs(throttle), 0, 1) : 0;
        _maneuverTarget = active ? Math.Clamp(maneuver, 0, 1) : 0;
        _brakeTarget = active && brake ? 1 : 0;
        _boostTarget = active && boost && _thrustTarget > .05 ? 1 : 0;
        _cruiseTarget = active && cruise ? 1 : 0;
    }

    public (float Left, float Right) NextFrame()
    {
        _active += (_activeTarget - _active) * Fast;
        _thrust += (_thrustTarget - _thrust) * (_thrustTarget > _thrust ? Attack : Release);
        _maneuver += (_maneuverTarget - _maneuver) * Fast;
        _boost += (_boostTarget - _boost) * (_boostTarget > _boost ? Fast : Slow);
        _cruise += (_cruiseTarget - _cruise) * Slow;
        _brake += (_brakeTarget - _brake) * Fast;
        var noise = Noise();
        _low += (noise - _low) * LowFilter;
        _body += (noise - _body) * BodyFilter;
        _air += (noise - _air) * AirFilter;
        _side += (Noise() - _side) * SideFilter;
        _corePhase = Advance(_corePhase, 39 + _thrust * 9 + _boost * 3);
        _turbinePhase = Advance(_turbinePhase, 164 + _thrust * 47 + _cruise * 32);
        _pulsePhase = Advance(_pulsePhase, 1.3 + _boost * 2.1);
        var pulse = 1 + Math.Sin(_pulsePhase) * (.025 + _boost * .065);
        var load = _thrust + _brake * .55;
        // Wide, filtered turbulence dominates the restrained reactor/turbine resonances.
        var rumble = _low * (.035 + load * .56 + _boost * .28 + _cruise * .13);
        var combustion = (_body - _low) * (load * .23 + _boost * .18 + _brake * .14);
        var plasma = (_air - _body) * (.018 * load + .085 * _boost + .038 * _maneuver + .025 * _brake);
        var core = Math.Sin(_corePhase) * (.003 + load * .009) + Math.Sin(2 * _corePhase) * load * .003;
        var turbine = Math.Sin(_turbinePhase + _body * 2) * (.003 * load + .006 * _cruise);
        var common = (rumble + combustion + plasma + core + turbine) * pulse;
        var width = _side * (.012 * load + .022 * _boost + .04 * _maneuver);
        return ((float)((common + width) * _active), (float)((common - width) * _active));
    }

    private double Noise()
    {
        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
        return _random / (double)uint.MaxValue * 2 - 1;
    }
    private static double Advance(double phase, double frequency) => (phase + Math.Tau * frequency / SampleRate) % Math.Tau;
    private static double Filter(double frequency) => 1 - Math.Exp(-Math.Tau * frequency / SampleRate);
    private static double FilterTime(double seconds) => 1 - Math.Exp(-1 / (seconds * SampleRate));
}
