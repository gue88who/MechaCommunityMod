namespace MechaCommunityMod.Plugins.BattleStatistics;

// One hit-effect call can hit multiple actors (e.g. an air drop). Inner effect
// calls have their own scope; only a matching taken event consumes an excess.
internal sealed class OverkillHit
{
    internal long Manager { get; }
    internal DamageSource? ContextSource { get; set; }
    internal long ContextTarget { get; set; }
    private Dictionary<long, long>? _excess;

    internal OverkillHit(long manager) => Manager = manager;

    internal void Observe(long target, int lifeBefore, int damageAtHealth, int healthLost)
    {
        var amount = OverkillStore.LethalExcess(lifeBefore, damageAtHealth, healthLost);
        if (amount <= 0) return;
        _excess ??= new();
        _excess[target] = checked(_excess.GetValueOrDefault(target) + amount);
    }

    internal long Consume(long target, int incoming)
    {
        if (_excess is null || !_excess.Remove(target, out var amount)) return 0;
        return Math.Min(amount, Math.Max(0L, incoming));
    }
}
