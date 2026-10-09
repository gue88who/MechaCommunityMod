namespace MechaCommunityMod.Plugins.BattleStatistics;

// Native GetUnitDatas omits temporary recorders (spawned and converted actors).
// Capture their native deltas with the team at the event, never the actor's
// potentially restored team when the result is opened.
internal sealed class LateUnitStats
{
    private MatchIdentity? _identity;
    private readonly Dictionary<(long Round, int Team, long Recorder), DamageSample> _stats = new();
    internal void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity;
        _stats.Clear();
    }
    internal void Add(long round, int team, long recorder, UnitKey unit, long damage, long taken, long kills)
    {
        var key = (round, team, recorder);
        var old = _stats.GetValueOrDefault(key);
        _stats[key] = new(unit, checked(old.Damage + Math.Max(0, damage)), checked(old.Kills + Math.Max(0, kills)),
            checked(old.DamageTaken + Math.Max(0, taken)));
    }
    internal IEnumerable<(long Recorder, DamageSample Sample)> Read(long round, int team, ISet<long> nativeRecorders)
        => _stats.Where(p => p.Key.Round == round && p.Key.Team == team && !nativeRecorders.Contains(p.Key.Recorder))
            .Select(p => (p.Key.Recorder, p.Value));
    internal void RetainRounds(IEnumerable<long> rounds)
    {
        var keep = rounds.ToHashSet();
        foreach (var key in _stats.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) _stats.Remove(key);
    }
}
