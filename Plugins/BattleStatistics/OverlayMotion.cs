namespace MechaCommunityMod.Plugins.BattleStatistics;

// Short, finite transitions driven by unscaled frame time. Retarget from the
// current value so a quick key release/repress never flashes or jumps.
internal sealed class OverlayMotion
{
    private float _from, _target, _elapsed, _duration;
    internal float Value { get; private set; }

    internal void Snap(float value)
    {
        Value = _from = _target = value;
        _elapsed = _duration = 0;
    }

    internal void Target(float value, float duration)
    {
        if (value == _target) return;
        _from = Value;
        _target = value;
        _elapsed = 0;
        _duration = duration;
        if (duration <= 0) Snap(value);
    }

    internal bool Advance(float seconds)
    {
        if (Value == _target || seconds <= 0) return false;
        _elapsed = Math.Min(_duration, _elapsed + seconds);
        var t = _duration > 0 ? _elapsed / _duration : 1;
        Value = t >= 1 ? _target : _from + (_target - _from) * (t * t * (3 - 2 * t));
        return true;
    }
}
