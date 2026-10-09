namespace MechaCommunityMod.Plugins.BattleStatistics;

// Keys identify native round snapshots and their normalized damage recorders.
// Only numeric identities are retained, never native objects.
internal sealed class OverkillStore
{
    private MatchIdentity? _identity;
    private readonly Dictionary<(long Round, long Recorder, int Team, bool Dealt), long> _amounts = new();

    internal void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity;
        _amounts.Clear();
    }

    internal void Add(long round, long recorder, int team, long amount, bool dealt = false)
    {
        if (amount <= 0) return;
        var key = (round, recorder, team, dealt);
        _amounts[key] = checked(_amounts.GetValueOrDefault(key) + amount);
    }

    internal long Get(long round, long recorder, int team, bool dealt = false) => _amounts.GetValueOrDefault((round, recorder, team, dealt));

    internal void RetainRounds(IEnumerable<long> rounds)
    {
        var keep = rounds.ToHashSet();
        foreach (var key in _amounts.Keys.Where(k => !keep.Contains(k.Round)).ToArray())
            _amounts.Remove(key);
    }

    // Observe damage at the final health clamp, AFTER armor/shields, and require
    // the returned health loss to confirm a lethal hit. Immunity is not overkill.
    internal static long LethalExcess(int lifeBefore, int damageAtHealth, int healthLost) =>
        lifeBefore > 0 && healthLost >= lifeBefore ? Math.Max(0L, (long)damageAtHealth - lifeBefore) : 0;
}
