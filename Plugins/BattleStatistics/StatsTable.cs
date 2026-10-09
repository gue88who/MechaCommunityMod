namespace MechaCommunityMod.Plugins.BattleStatistics;

internal enum StatsColumn { Kills, Damage, DamageOverkill, Tanked, TankedOverkill, Hacked, XpGained, XpFed, Supply, CoreDamage, HackAmount, FailedHack }
internal sealed record StatsTableRow(string Id, UnitKey Unit, bool Right, string Name,
    long? Kills, long? Damage, long? DamageOverkill, long? Tanked, long? TankedOverkill,
    long? Hacked, double? XpGained, double? XpFed, long? Supply, bool Shared = false,
    string? ParentId = null, bool Spawned = false, int Depth = 0, bool Inclusive = false)
{
    internal SourceDamage[] DealtSources { get; init; } = Array.Empty<SourceDamage>();
    internal SourceDamage[] TakenSources { get; init; } = Array.Empty<SourceDamage>();
    internal string[] MapIds { get; init; } = Array.Empty<string>();
    internal bool Breakdown { get; init; }
    internal DamageSource? Source { get; init; }
    internal bool Incoming { get; init; }
    internal bool? SourceRight { get; init; }
    internal long? CoreDamage { get; init; }
    internal long? HackAmount { get; init; }
    internal long? FailedHack { get; init; }
    internal bool HasAttributedUnits { get; init; }
    internal SquadStats[] LevelSquads { get; init; } = Array.Empty<SquadStats>();
    internal bool Grouped { get; init; }
    internal bool HasLevelBadge => !Grouped && !Breakdown && Unit.Kind == 0;
    internal bool AccentRight => Breakdown ? SourceRight ?? (Incoming ? !Right : Right) : Right;
    internal bool HasDetails => !Breakdown && Unit.Kind < 100 && (DealtSources.Length > 0 || TakenSources.Length > 0);
    internal double? Value(StatsColumn column) => column switch {
        StatsColumn.Kills => Kills, StatsColumn.Damage => Damage, StatsColumn.DamageOverkill => DamageOverkill,
        StatsColumn.Tanked => Tanked, StatsColumn.TankedOverkill => TankedOverkill, StatsColumn.Hacked => Hacked,
        StatsColumn.HackAmount => HackAmount, StatsColumn.FailedHack => FailedHack,
        StatsColumn.XpGained => XpGained, StatsColumn.XpFed => XpFed, StatsColumn.CoreDamage => CoreDamage, _ => Supply };
}

internal static class StatsTable
{
    internal static string GroupName(string name, IEnumerable<string> ids)
    {
        var count = ids.Distinct().Count();
        if (count == 0) return name;
        var plural = count <= 1 ? name : name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("x", StringComparison.OrdinalIgnoreCase) ? name + "es" : name + "s";
        return $"{plural} x{count}";
    }

    internal static StatsTableRow[] Rows(TeamStats team, bool right, bool individuals,
        IReadOnlyDictionary<UnitKey, UnitCost>? costs, Func<UnitKey, string> describe, Func<UnitKey, bool>? namedUnits = null,
        IReadOnlyDictionary<string, UnitCost>? squadCosts = null, bool groupChildren = true)
    {
        var rows = new List<StatsTableRow>();
        foreach (var unit in team.Rows.Select(r => r.Unit).Union(costs?.Keys ?? Enumerable.Empty<UnitKey>()))
        {
            var row = team.Rows.FirstOrDefault(r => r.Unit == unit) ?? new DamageRow(unit, 0, 0, 0);
            var name = unit.Kind == 102 && row.Casts > 0 ? $"{describe(unit)} x{row.Casts}" : describe(unit);
            long? supply = costs is not null && costs.TryGetValue(unit, out var cost) ? cost.Total : null;
            var split = row.Squads is { Length: > 0 } && (individuals || row.Squads.Any(s => s.Spawned || s.ParentId is not null))
                && (namedUnits?.Invoke(unit) ?? true);
            if (!split)
            {
                rows.Add(new($"type:{unit}", unit, right, name, row.Kills, row.Damage, row.DealtOverkill,
                    row.DamageTaken - row.Overkill, row.Overkill, row.Hacking.Converted,
                    row.XpEarned, row.EnemyXpAwarded, supply) { DealtSources = row.Sources(true), TakenSources = row.Sources(false), CoreDamage = row.CoreDamage,
                        HackAmount = row.Hacking.Successful, FailedHack = row.Hacking.Failed,
                        Grouped = true, Name = unit.Kind == 0 ? GroupName(name, (row.Squads ?? Array.Empty<SquadStats>()).Select(s => s.Id)) : name,
                        LevelSquads = row.Squads ?? Array.Empty<SquadStats>() });
                continue;
            }
            // Split preserves partially observed totals and refuses inconsistent snapshots.
            var parts = SquadPresentation.Split(new TeamStats(new[] { row }, row.Damage, row.Kills, row.DamageTaken), false, describe).Rows;
            if (parts.Length == 1 && parts[0].DisplayName is null)
            {
                rows.Add(new($"type:{unit}", unit, right, name, row.Kills, row.Damage, row.DealtOverkill,
                    row.DamageTaken - row.Overkill, row.Overkill, row.Hacking.Converted,
                    row.XpEarned, row.EnemyXpAwarded, supply) { DealtSources = row.Sources(true), TakenSources = row.Sources(false), CoreDamage = row.CoreDamage,
                        HackAmount = row.Hacking.Successful, FailedHack = row.Hacking.Failed,
                        Grouped = true, Name = unit.Kind == 0 ? GroupName(name, (row.Squads ?? Array.Empty<SquadStats>()).Select(s => s.Id)) : name,
                        LevelSquads = row.Squads ?? Array.Empty<SquadStats>() });
                continue;
            }
            foreach (var part in parts.Where(p => individuals || p.Squads?.Any(s => s.Spawned || s.ParentId is not null) == true))
            {
                var squad = part.Squads?.FirstOrDefault();
                rows.Add(new(squad?.Id ?? $"remainder:{unit}", unit, right,
                    squad?.Spawned == true ? name : part.DisplayName ?? name, part.Kills, part.Damage, part.DealtOverkill,
                    part.DamageTaken - part.Overkill, part.Overkill, squad?.Hacked, part.XpEarned, part.EnemyXpAwarded,
                    squad is not null && squadCosts?.TryGetValue(squad.Id, out var investment) == true ? investment.Purchase + investment.Upgrade : null,
                    ParentId: squad?.ParentId is not null ? (individuals ? squad.ParentId : squad.ParentUnit is { } parent ? $"type:{parent}" : null) : null,
                    Spawned: squad?.Spawned == true) { DealtSources = part.Sources(true), TakenSources = part.Sources(false),
                        MapIds = squad is null ? Array.Empty<string>() : new[] { squad.Id }, CoreDamage = squad?.CoreDamage,
                        HackAmount = squad?.Hacking?.Successful, FailedHack = squad?.Hacking?.Failed,
                        LevelSquads = squad is null ? Array.Empty<SquadStats>() : new[] { squad } });
            }
            if (!individuals)
            {
                var own = parts.Where(p => p.Squads?.Any(s => s.Spawned || s.ParentId is not null) != true).ToArray();
                var children = parts.Except(own).SelectMany(p => p.Squads ?? Array.Empty<SquadStats>()).ToArray();
                if (own.Length > 0 || supply is > 0 || row.Hacking.Converted > 0)
                    rows.Add(new($"type:{unit}", unit, right, name, own.Sum(p => p.Kills), own.Sum(p => p.Damage), own.Sum(p => p.DealtOverkill),
                        own.Sum(p => p.DamageTaken - p.Overkill), own.Sum(p => p.Overkill),
                        children.All(s => s.Hacked is not null) ? row.Hacking.Converted - children.Sum(s => s.Hacked ?? 0) : null,
                        own.Sum(p => p.XpEarned), own.Sum(p => p.EnemyXpAwarded), supply) {
                            Grouped = true, Name = GroupName(name, own.SelectMany(p => p.Squads ?? Array.Empty<SquadStats>()).Select(s => s.Id)),
                            LevelSquads = own.SelectMany(p => p.Squads ?? Array.Empty<SquadStats>()).ToArray(),
                            DealtSources = DamageSources.Merge(own.SelectMany(p => p.Sources(true))),
                            TakenSources = DamageSources.Merge(own.SelectMany(p => p.Sources(false))),
                            HackAmount = children.All(s => s.Hacking is not null) ? row.Hacking.Successful - children.Sum(s => s.Hacking!.Value.Successful) : null,
                            FailedHack = children.All(s => s.Hacking is not null) ? row.Hacking.Failed - children.Sum(s => s.Hacking!.Value.Failed) : null,
                            CoreDamage = own.All(p => p.CoreDamage is not null) ? own.Sum(p => p.CoreDamage ?? 0) : null });
                continue;
            }
            // Type-level investment/hacking remains in recorded totals. Do not invent
            // an allocation or add artificial unit rows in individual mode.
        }
        // One child row per spawning squad and spawned type, not a nickname for each mine.
        if (!groupChildren) return rows.ToArray();
        return GroupAttributed(rows, describe);
    }

    internal static StatsTableRow[] GroupAttributed(IEnumerable<StatsTableRow> values, Func<UnitKey, string> describe,
        bool attachedOnly = false)
    {
        var rows = values.ToArray();
        var grouped = rows.Where(r => r.ParentId is not null && (!attachedOnly || r.Depth > 0)).ToArray();
        var groupedIds = grouped.Select(r => (r.Right, r.Id)).ToHashSet();
        var children = grouped
            .GroupBy(r => (r.Right, r.ParentId, r.Unit, r.Spawned)).Select(g => new {
                Originals = g.Select(r => (r.Right, r.Id)).ToArray(),
                Row = g.First() with { Id = $"child:{g.Key.ParentId}:{g.Key.Unit}:{g.Key.Spawned}",
                    LevelSquads = g.SelectMany(r => r.LevelSquads).ToArray(),
                    MapIds = g.SelectMany(r => r.MapIds.Length > 0 ? r.MapIds : new[] { r.Id }).Distinct().ToArray(),
                    Grouped = true,
                    Name = GroupName(describe(g.Key.Unit), g.SelectMany(r => r.MapIds.Length > 0 ? r.MapIds : new[] { r.Id })),
                    Kills = g.Sum(r => r.Kills), Damage = g.Sum(r => r.Damage), DamageOverkill = g.Sum(r => r.DamageOverkill),
                    Tanked = g.Sum(r => r.Tanked), TankedOverkill = g.Sum(r => r.TankedOverkill),
                    Hacked = g.Any(r => r.Hacked is not null) ? g.Sum(r => r.Hacked ?? 0) : null,
                    HackAmount = g.All(r => r.HackAmount is not null) ? g.Sum(r => r.HackAmount ?? 0) : null,
                    FailedHack = g.All(r => r.FailedHack is not null) ? g.Sum(r => r.FailedHack ?? 0) : null,
                    XpGained = g.Sum(r => r.XpGained), XpFed = g.Sum(r => r.XpFed),
                    CoreDamage = g.All(r => r.CoreDamage is not null) ? g.Sum(r => r.CoreDamage ?? 0) : null,
                    DealtSources = DamageSources.Merge(g.SelectMany(r => r.DealtSources)),
                    TakenSources = DamageSources.Merge(g.SelectMany(r => r.TakenSources)) } }).ToArray();
        var aliases = children.SelectMany(c => c.Originals.Select(id => (id, c.Row.Id))).ToDictionary(p => p.id, p => p.Id);
        return rows.Where(r => !groupedIds.Contains((r.Right, r.Id))).Concat(children.Select(c => c.Row))
            .Select(r => r.ParentId is { } parent && aliases.TryGetValue((r.Right, parent), out var replacement) ? r with { ParentId = replacement } : r).ToArray();
    }

    internal static StatsTableRow[] Sort(IEnumerable<StatsTableRow> rows, StatsColumn? column, bool descending,
        bool player = false)
    {
        var input = rows.ToArray();
        bool EmptyCopy(StatsTableRow row) => row.Id.StartsWith("deployed:", StringComparison.Ordinal)
            && row.ParentId is null && !row.Spawned && row.Supply is null
            && row.Kills.GetValueOrDefault() == 0 && row.Damage.GetValueOrDefault() == 0
            && row.DamageOverkill.GetValueOrDefault() == 0 && row.Tanked.GetValueOrDefault() == 0
            && row.TankedOverkill.GetValueOrDefault() == 0 && row.Hacked.GetValueOrDefault() == 0
            && row.HackAmount.GetValueOrDefault() == 0 && row.FailedHack.GetValueOrDefault() == 0
            && row.XpGained.GetValueOrDefault() == 0 && row.XpFed.GetValueOrDefault() == 0 && row.CoreDamage.GetValueOrDefault() == 0;
        // Also repair the presentation of reports captured before the reader fix.
        // Preserve idle owned squads (with supply) and any conversion activity.
        var active = input.Where(r => !EmptyCopy(r)).Select(r => (r.Right, r.Id)).ToHashSet();
        var all = input.Where(r => !EmptyCopy(r) || !active.Contains((!r.Right, r.Id))).ToArray();
        var keys = all.Select(r => (r.Right, r.Id)).ToHashSet();
        var byId = all.ToDictionary(r => (r.Right, r.Id));
        bool Attached(StatsTableRow row)
        {
            if (row.ParentId is null || !keys.Contains((row.Right, row.ParentId))) return false;
            var seen = new HashSet<string> { row.Id };
            while (row.ParentId is { } parent && byId.TryGetValue((row.Right, parent), out var next))
            { if (!seen.Add(parent)) return false; row = next; }
            return true;
        }
        var children = all.Where(Attached)
            .ToLookup(r => (r.Right, r.ParentId!));
        var totals = new Dictionary<(bool, string), StatsTableRow>();
        static long? Sum(long? a, long? b) => a is null && b is null ? null : checked((a ?? 0) + (b ?? 0));
        static long? Hack(long? a, long? b) => a is null || b is null ? null : checked(a.Value + b.Value);
        static double? Xp(double? a, double? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
        StatsTableRow Total(StatsTableRow row)
        {
            if (totals.TryGetValue((row.Right, row.Id), out var saved)) return saved;
            var value = row;
            // A spell's own combat effect has no survivor score. Its summoned
            // units can contribute core damage; missing child values stay unknown.
            if (!row.Inclusive && row.Unit.Kind >= 100 && children[(row.Right, row.Id)].Any())
                value = row with { CoreDamage = row.CoreDamage ?? 0 };
            if (!row.Inclusive)
                foreach (var child in children[(row.Right, row.Id)])
                {
                    var contribution = Total(child);
                    value = value with { Kills = Sum(value.Kills, contribution.Kills), Damage = Sum(value.Damage, contribution.Damage),
                        DamageOverkill = Sum(value.DamageOverkill, contribution.DamageOverkill), Tanked = Sum(value.Tanked, contribution.Tanked),
                        TankedOverkill = Sum(value.TankedOverkill, contribution.TankedOverkill), Hacked = Sum(value.Hacked, contribution.Hacked),
                        HackAmount = Hack(value.HackAmount, contribution.HackAmount), FailedHack = Hack(value.FailedHack, contribution.FailedHack),
                        XpGained = Xp(value.XpGained, contribution.XpGained), XpFed = Xp(value.XpFed, contribution.XpFed), Supply = Sum(value.Supply, contribution.Supply),
                        CoreDamage = contribution.CoreDamage is null || value.CoreDamage is null
                            ? null : Sum(value.CoreDamage, contribution.CoreDamage) };
                    value = value with { DealtSources = DamageSources.Merge(value.DealtSources.Concat(contribution.DealtSources)),
                        TakenSources = DamageSources.Merge(value.TakenSources.Concat(contribution.TakenSources)) };
                }
            return totals[(row.Right, row.Id)] = value with { Inclusive = true };
        }
        IOrderedEnumerable<StatsTableRow> Order(IEnumerable<StatsTableRow> values) => (column is { } metric
            ? values.OrderBy(r => r.Value(metric) is null).ThenBy(r => descending ? -r.Value(metric) : r.Value(metric))
            : player ? values.OrderBy(r => descending ? !r.Right : r.Right)
            : descending ? values.OrderByDescending(r => r.Unit.Kind).ThenByDescending(r => r.Unit.Id)
                : values.OrderBy(r => r.Unit.Kind).ThenBy(r => r.Unit.Id))
            .ThenBy(r => r.Unit.Kind).ThenBy(r => r.Unit.Id).ThenBy(r => r.Right).ThenBy(r => r.Id, StringComparer.Ordinal);
        var result = new List<StatsTableRow>(); var visited = new HashSet<(bool, string)>();
        void Append(StatsTableRow row, int depth)
        {
            if (!visited.Add((row.Right, row.Id))) return;
            result.Add(Total(row) with { Depth = depth });
            foreach (var child in Order(children[(row.Right, row.Id)].Select(Total))) Append(child, depth + 1);
        }
        foreach (var row in Order(all.Where(r => !Attached(r)).Select(Total))) Append(row, 0);
        // Corrupt/cyclic references cannot hide recorded damage.
        foreach (var row in Order(all)) Append(row, 0);
        return result.ToArray();
    }
    internal static StatsTableRow[] Collapse(IEnumerable<StatsTableRow> rows, ISet<(bool Right, string Id)> expanded,
        bool showAttributedUnits = true)
    {
        // Sorting already calculated inclusive totals. Visibility never recalculates
        // them, so collapsing children cannot change a parent's score or ordering.
        var all = rows.ToArray();
        var byId = all.ToDictionary(r => (r.Right, r.Id));
        bool Parent(StatsTableRow row, out StatsTableRow parent)
        {
            parent = null!;
            return row.Depth > 0 && row.ParentId is { } id && byId.TryGetValue((row.Right, id), out parent!)
                && parent.Depth < row.Depth;
        }
        var parents = all.Where(r => Parent(r, out _)).Select(r => (r.Right, r.ParentId!)).ToHashSet();
        var visible = new Dictionary<(bool Right, string Id), StatsTableRow>();
        var result = new List<StatsTableRow>();
        foreach (var row in all)
        {
            var depth = 0;
            if (Parent(row, out var parent))
            {
                var key = (parent.Right, parent.Id);
                if (!showAttributedUnits || !expanded.Contains(key) || !visible.TryGetValue(key, out var shownParent)) continue;
                depth = shownParent.Depth + 1;
            }
            // A type filter can hide the parent. Keep the selected child type as
            // a standalone row, just as records with missing provenance stay visible.
            var shown = row with { Depth = depth, HasAttributedUnits = showAttributedUnits && parents.Contains((row.Right, row.Id)) };
            result.Add(shown); visible[(row.Right, row.Id)] = shown;
        }
        return result.ToArray();
    }

    internal static StatsTableRow[] Details(IEnumerable<StatsTableRow> rows, ISet<(bool Right, string Id)> expanded)
    {
        var result = new List<StatsTableRow>();
        foreach (var row in rows)
        {
            result.Add(row);
            if (!row.HasDetails || !expanded.Contains((row.Right, row.Id))) continue;
            foreach (var incoming in new[] { false, true })
            {
                foreach (var source in DamageSources.Merge(incoming ? row.TakenSources : row.DealtSources))
                    result.Add(new($"source:{row.Id}:{incoming}:{source.SourceRight}:{source.Source.Category}:{source.Source.Id}:{source.Source.Name}",
                        row.Unit, row.Right, source.Source.Name, null, incoming ? null : source.Damage, incoming ? null : source.Overkill,
                        incoming ? source.Damage : null, incoming ? source.Overkill : null, null, null, null, null,
                        ParentId: row.Id, Depth: row.Depth + 1, Inclusive: true) { Breakdown = true, Source = source.Source, Incoming = incoming, SourceRight = source.SourceRight });
            }
        }
        return result.ToArray();
    }
    internal static bool Useful(StatsTableRow row) => row.Unit.Kind != 100 + (int)DamageCategory.Spell
        || row.Damage is > 0 || row.DamageOverkill is > 0 || row.Tanked is > 0 || row.TankedOverkill is > 0 || row.Kills is > 0 || row.CoreDamage is > 0;
}
