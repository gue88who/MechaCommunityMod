namespace MechaCommunityMod.Plugins.BattleStatistics;

internal readonly record struct HackStats(long Successful = 0, long Failed = 0, long Pending = 0, long Converted = 0)
{
    internal long Total => Successful + Failed + Pending;
    internal static HackStats Sum(IEnumerable<HackStats> values) => values.Aggregate(new HackStats(),
        (a, b) => new(a.Successful + b.Successful, a.Failed + b.Failed, a.Pending + b.Pending, a.Converted + b.Converted));
}
internal enum UnitOrigin { Deployed, Spawned, Hacked }
internal sealed record Contribution(UnitOrigin Origin, UnitKey Unit, UnitKey? Parent, bool Child,
    long Damage = 0, long Taken = 0, long Overkill = 0, long TakenOverkill = 0, long Kills = 0, string Tech = "")
{
    internal static Contribution[] Merge(IEnumerable<Contribution> values) => values
        .GroupBy(v => (v.Origin, v.Unit, v.Parent, v.Child, v.Tech))
        .Select(g => g.First() with { Damage = g.Sum(v => v.Damage), Taken = g.Sum(v => v.Taken),
            Overkill = g.Sum(v => v.Overkill), TakenOverkill = g.Sum(v => v.TakenOverkill), Kills = g.Sum(v => v.Kills) }).ToArray();
}

internal sealed class HackLedger
{
    private sealed record Attempt(int Team, UnitKey Unit, long Amount, string? Squad);
    private readonly Dictionary<(long Source, long Target), Attempt> _pending = new();
    private readonly Dictionary<(int Team, UnitKey Unit, string? Squad), HackStats> _done = new();
    private readonly HashSet<long> _converting = new();
    private readonly HashSet<(long Source, long Target)> _deferredEnds = new();
    internal void BeginConversion(long target) => _converting.Add(target);
    internal void EndConversion(long target)
    {
        _converting.Remove(target);
        foreach (var key in _deferredEnds.Where(k => k.Target == target).ToArray())
        {
            _deferredEnds.Remove(key);
            End(key.Source, key.Target);
        }
    }
    internal void Add(long source, long target, int team, UnitKey unit, long amount, string? squad = null)
    {
        var key = (source, target);
        if (_pending.TryGetValue(key, out var old) && (old.Team != team || old.Unit != unit || old.Squad != squad)) End(source, target);
        _pending[key] = new(team, unit, checked((_pending.GetValueOrDefault(key)?.Amount ?? 0) + Math.Max(0, amount)), squad);
    }
    internal void End(long source, long target)
    {
        // ChangeTeam clears the old target before ownership changes. Keep that
        // cleanup pending until the conversion either commits or aborts.
        if (_converting.Contains(target)) { _deferredEnds.Add((source, target)); return; }
        if (!_pending.Remove((source, target), out var value)) return;
        var key = (value.Team, value.Unit, value.Squad); var total = _done.GetValueOrDefault(key);
        _done[key] = total with { Failed = checked(total.Failed + value.Amount) };
    }
    internal (long Actor, UnitKey Unit)? Complete(long target, int team)
    {
        (long, UnitKey)? owner = null;
        foreach (var pair in _pending.Where(p => p.Key.Target == target).ToArray())
        {
            if (pair.Value.Team != team) { End(pair.Key.Source, target); continue; }
            var value = pair.Value; var key = (team, value.Unit, value.Squad); var total = _done.GetValueOrDefault(key);
            _done[key] = total with { Successful = checked(total.Successful + value.Amount), Converted = total.Converted + (owner is null ? 1 : 0) };
            owner ??= (pair.Key.Source, value.Unit);
            _pending.Remove(pair.Key);
            _deferredEnds.Remove(pair.Key);
        }
        return owner;
    }
    internal void Died(long actor)
    {
        foreach (var key in _pending.Keys.Where(k => k.Source == actor || k.Target == actor).ToArray()) End(key.Source, key.Target);
    }
    internal void Finish()
    {
        foreach (var key in _pending.Keys.ToArray()) End(key.Source, key.Target);
    }
    internal Dictionary<UnitKey, HackStats> Snapshot(int team)
    {
        var result = _done.Where(p => p.Key.Team == team).GroupBy(p => p.Key.Unit)
            .ToDictionary(g => g.Key, g => HackStats.Sum(g.Select(p => p.Value)));
        foreach (var value in _pending.Values.Where(v => v.Team == team))
        {
            var total = result.GetValueOrDefault(value.Unit);
            result[value.Unit] = total with { Pending = total.Pending + value.Amount };
        }
        return result;
    }
    internal Dictionary<string, HackStats> Squads(int team)
    {
        // Capture stable squad IDs when progress happens, before actor pooling or
        // conversion can change the native recorder's identity or team.
        var result = _done.Where(p => p.Key.Team == team && p.Key.Squad is not null).GroupBy(p => p.Key.Squad!)
            .ToDictionary(g => g.Key, g => HackStats.Sum(g.Select(p => p.Value)));
        foreach (var value in _pending.Values.Where(v => v.Team == team && v.Squad is not null))
        {
            var total = result.GetValueOrDefault(value.Squad!);
            result[value.Squad!] = total with { Pending = checked(total.Pending + value.Amount) };
        }
        return result;
    }
}

internal sealed class ContributionLedger
{
    private sealed record Origin(UnitOrigin Kind, UnitKey Unit, UnitKey? Parent, int ParentTeam, string Tech);
    private readonly Dictionary<long, Origin> _origins = new();
    private readonly Dictionary<(int Team, UnitKey Unit), List<Contribution>> _totals = new();
    private readonly Dictionary<(int Team, UnitKey Unit), List<SourceDamage>> _supplement = new();
    private readonly Dictionary<(int Team, UnitKey Unit), long> _extraKills = new();
    internal HackLedger Hacks { get; } = new();
    internal void Spawn(long actor, UnitKey unit, UnitKey? parent, int team, string tech = "")
    {
        if (_origins.TryGetValue(actor, out var prior) && prior.Kind == UnitOrigin.Spawned && prior.Unit == unit)
        {
            parent ??= prior.Parent;
            if (string.IsNullOrEmpty(tech)) tech = prior.Tech;
        }
        _origins[actor] = new(UnitOrigin.Spawned, unit, parent, team, tech);
        Hit(actor, team, unit, true, 0, 0);
    }
    internal (long Actor, UnitKey Unit)? Convert(long actor, UnitKey unit, int team)
    {
        var parent = Hacks.Complete(actor, team);
        // Battle cleanup also restores teams. Only an observed completed hack
        // creates conversion statistics or a Hacker child reference.
        if (parent is null) return null;
        _origins[actor] = new(UnitOrigin.Hacked, unit, parent?.Unit, team, "");
        Hit(actor, team, unit, true, 0, 0);
        return parent;
    }
    internal void Hit(long actor, int team, UnitKey unit, bool dealt, long damage, long overkill, long kills = 0)
    {
        var origin = _origins.GetValueOrDefault(actor);
        var row = new Contribution(origin?.Kind ?? UnitOrigin.Deployed, unit, origin?.Parent, false,
            dealt ? damage : 0, dealt ? 0 : damage, dealt ? overkill : 0, dealt ? 0 : overkill, dealt ? kills : 0, origin?.Tech ?? "");
        Add(team, unit, row);
        if (origin?.Parent is { } parent && origin.ParentTeam == team)
            Add(team, parent, row with { Child = true });
    }
    private void Add(int team, UnitKey unit, Contribution row)
    {
        var key = (team, unit);
        if (!_totals.TryGetValue(key, out var list)) _totals[key] = list = new();
        for (var i = 0; i < list.Count; i++)
        {
            var previous = list[i];
            if (previous.Origin != row.Origin || previous.Unit != row.Unit || previous.Parent != row.Parent
                || previous.Child != row.Child || previous.Tech != row.Tech) continue;
            // Same attribution key: direct addition avoids a LINQ grouping,
            // temporary arrays and five repeated enumerations on every hit.
            list[i] = previous with { Damage = checked(previous.Damage + row.Damage),
                Taken = checked(previous.Taken + row.Taken), Overkill = checked(previous.Overkill + row.Overkill),
                TakenOverkill = checked(previous.TakenOverkill + row.TakenOverkill), Kills = checked(previous.Kills + row.Kills) };
            return;
        }
        list.Add(row);
    }
    internal void Supplement(int team, UnitKey unit, DamageSource effect, long damage, long overkill, long kills)
    {
        var key = (team, unit);
        if (!_supplement.TryGetValue(key, out var list)) _supplement[key] = list = new();
        var index = list.FindIndex(s => s.Source == effect);
        var old = index < 0 ? new SourceDamage(effect, 0, 0) : list[index];
        var next = old with { Damage = old.Damage + damage, Overkill = old.Overkill + overkill };
        if (index < 0) list.Add(next); else list[index] = next;
        _extraKills[key] = _extraKills.GetValueOrDefault(key) + kills;
    }
    internal TeamStats Enrich(TeamStats team, int index)
    {
        var rows = team.Rows.ToDictionary(r => r.Unit);
        var hacks = Hacks.Snapshot(index);
        var squadHacks = Hacks.Squads(index);
        foreach (var pair in _supplement.Where(p => p.Key.Team == index))
        {
            var unit = pair.Key.Unit; var row = rows.GetValueOrDefault(unit) ?? new DamageRow(unit, 0, 0, 0);
            rows[unit] = row with { Damage = row.Damage + pair.Value.Sum(s => s.Damage),
                DealtOverkill = row.DealtOverkill + pair.Value.Sum(s => s.Overkill), Kills = row.Kills + _extraKills.GetValueOrDefault(pair.Key),
                DealtSources = DamageSources.Merge(row.Sources(true).Concat(pair.Value)) };
        }
        foreach (var unit in hacks.Keys.Concat(_totals.Keys.Where(k => k.Team == index).Select(k => k.Unit)).Distinct())
        {
            // Native counters already contain spawned and converted damage.
            // Parent contributions are references, never added to army totals.
            var row = rows.GetValueOrDefault(unit) ?? new DamageRow(unit, 0, 0, 0);
            rows[unit] = row with { Hacking = hacks.GetValueOrDefault(unit), Contributions = _totals.GetValueOrDefault((index, unit))?.ToArray() };
        }
        foreach (var unit in rows.Keys.ToArray())
        {
            var row = rows[unit];
            if (row.Squads is not null)
                rows[unit] = row with { Squads = row.Squads.Select(s => s with {
                    Hacking = squadHacks.GetValueOrDefault(s.Id),
                    Hacked = squadHacks.TryGetValue(s.Id, out var observed) ? observed.Converted : s.Hacked }).ToArray() };
        }
        return new TeamStats(rows.Values.OrderByDescending(r => r.Damage + r.Hacking.Total).ToArray(),
            rows.Values.Sum(r => r.Damage), rows.Values.Sum(r => r.Kills), rows.Values.Sum(r => r.DamageTaken));
    }
}
