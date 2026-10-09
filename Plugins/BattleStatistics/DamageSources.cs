namespace MechaCommunityMod.Plugins.BattleStatistics;

internal enum DamageCategory { Attack, Tech, Spell, Other }
internal readonly record struct DamageSource(DamageCategory Category, int Id, string Name)
{
    internal static readonly DamageSource Attack = new(DamageCategory.Attack, 0, "Regular attacks");
    internal static readonly DamageSource Unknown = new(DamageCategory.Other, 0, "Unknown source");
    internal static readonly DamageSource Unobserved = new(DamageCategory.Other, -1, "History not observed");
    internal static readonly DamageSource Mismatch = new(DamageCategory.Other, -2, "Attribution mismatch");
}

internal static class DamageAttribution
{
    internal static DamageSource FillUnknown(DamageSource provider, DamageSource? effect, long effectOwner = 0, long hitOwner = 0) =>
        provider == DamageSource.Unknown && (effectOwner == 0 || effectOwner == hitOwner)
            && effect is { } known && known != DamageSource.Unknown ? known : provider;
    internal static DamageSource Resolve(DamageSource provider, DamageSource? context, long contextTarget, long target) =>
        context is { } source && source != DamageSource.Unknown && (contextTarget == 0 || contextTarget == target)
            ? source : provider;
}
internal sealed record SourceDamage(DamageSource Source, long Damage, long Overkill, bool? SourceRight = null)
{
    internal long Total => checked(Damage + Overkill);
}

// Values are managed snapshots; no native references survive a hit callback.
internal static class DamageSources
{
    internal static SourceDamage[] Merge(IEnumerable<SourceDamage> values) => values
        .GroupBy(x => (x.Source, x.SourceRight)).Select(g => new SourceDamage(g.Key.Source,
            g.Sum(x => Math.Max(0, x.Damage)), g.Sum(x => Math.Max(0, x.Overkill)), g.Key.SourceRight))
        .Where(x => x.Total > 0).OrderBy(x => x.Source.Category)
        .ThenByDescending(x => x.Total).ThenBy(x => x.Source.Name).ToArray();

    internal static SourceDamage[] Reconcile(IEnumerable<SourceDamage>? values, long damage, long overkill)
    {
        damage = Math.Max(0, damage);
        overkill = Math.Max(0, overkill);
        var rows = Merge(values ?? Array.Empty<SourceDamage>());
        var tracked = rows.Sum(x => x.Damage);
        var excess = rows.Sum(x => x.Overkill);
        // A replay seek or unsupported event must never invent source attribution.
        if (tracked > damage || excess > overkill)
            return damage + overkill == 0 ? Array.Empty<SourceDamage>()
                : new[] { new SourceDamage(DamageSource.Mismatch, damage, overkill) };
        return Merge(rows.Append(new SourceDamage(DamageSource.Unobserved, damage - tracked, overkill - excess)));
    }
}

internal sealed class DamageSourceStore
{
    private MatchIdentity? _identity;
    private readonly Dictionary<(long Round, long Recorder, int Team, bool Dealt), Dictionary<(DamageSource, bool?), SourceDamage>> _data = new();
    private readonly Dictionary<(long Round, int Team, DamageSource Source), (long Damage, long Overkill, long Kills)> _unowned = new();
    private readonly Dictionary<(long Round, int Team, DamageSource Source), HashSet<long>> _casts = new();
    internal void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity;
        _data.Clear();
        _unowned.Clear();
        _casts.Clear();
    }
    internal void Add(long round, long recorder, int team, bool dealt, DamageSource source, long damage, long overkill, bool? sourceRight = null)
    {
        if (damage <= 0 && overkill <= 0) return;
        var key = (round, recorder, team, dealt);
        if (!_data.TryGetValue(key, out var bucket)) _data[key] = bucket = new();
        var previous = bucket.GetValueOrDefault((source, sourceRight));
        bucket[(source, sourceRight)] = new SourceDamage(source,
            checked((previous?.Damage ?? 0) + Math.Max(0, damage)),
            checked((previous?.Overkill ?? 0) + Math.Max(0, overkill)), sourceRight);
    }
    internal SourceDamage[] Get(long round, long recorder, int team, bool dealt) =>
        _data.TryGetValue((round, recorder, team, dealt), out var bucket) ? bucket.Values.ToArray() : Array.Empty<SourceDamage>();
    internal void AddUnowned(long round, int team, DamageSource source, long damage, long overkill, long kills)
    {
        var key = (round, team, source);
        var previous = _unowned.GetValueOrDefault(key);
        _unowned[key] = (checked(previous.Damage + Math.Max(0, damage)),
            checked(previous.Overkill + Math.Max(0, overkill)), checked(previous.Kills + Math.Max(0, kills)));
    }
    internal void Cast(long round, int team, DamageSource source, long release)
    {
        var key = (round, team, source);
        if (!_casts.TryGetValue(key, out var casts)) _casts[key] = casts = new();
        casts.Add(release);
        // Keep shields, misses and interrupted casts visible even with no damage.
        _unowned.TryAdd(key, (0, 0, 0));
    }
    internal IEnumerable<(DamageSource Source, long Damage, long Overkill, long Kills, long Casts)> Unowned(long round, int team) =>
        _unowned.Where(p => p.Key.Round == round && p.Key.Team == team)
            .Select(p => (p.Key.Source, p.Value.Damage, p.Value.Overkill, p.Value.Kills, (long)(_casts.GetValueOrDefault(p.Key)?.Count ?? 0)));
    internal void RetainRounds(IEnumerable<long> rounds)
    {
        var keep = rounds.ToHashSet();
        foreach (var key in _data.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) _data.Remove(key);
        foreach (var key in _unowned.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) _unowned.Remove(key);
        foreach (var key in _casts.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) _casts.Remove(key);
    }
}
