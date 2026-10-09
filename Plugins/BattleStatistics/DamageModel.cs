namespace MechaCommunityMod.Plugins.BattleStatistics;

// Managed snapshots only: no game objects are retained by the presentation model.
internal readonly record struct UnitKey(int Kind, int Id);
internal readonly record struct DamageSample(UnitKey Unit, long Damage, long Kills, long DamageTaken = 0, long Overkill = 0, long DealtOverkill = 0, SourceDamage[]? DealtSources = null, SourceDamage[]? TakenSources = null, HackStats Hacking = default, Contribution[]? Contributions = null, long Casts = 0, SquadStats[]? Squads = null);
internal sealed record DamageRow(UnitKey Unit, long Damage, long Kills, long DamageTaken, long Overkill = 0, long DealtOverkill = 0, SourceDamage[]? DealtSources = null, SourceDamage[]? TakenSources = null)
{
    internal HackStats Hacking { get; init; }
    internal long Casts { get; init; }
    internal SquadStats[]? Squads { get; init; }
    internal string? DisplayName { get; init; }
    internal double XpEarned => Squads?.Sum(s => s.XpEarned) ?? 0;
    internal double EnemyXpAwarded => Squads?.Sum(s => s.EnemyXpAwarded) ?? 0;
    internal long? CoreDamage => Squads is { Length: > 0 } && Squads.All(s => s.CoreDamage is not null)
        ? Squads.Sum(s => s.CoreDamage ?? 0) : null;
    internal Contribution[]? Contributions { get; init; }
    internal long TotalDealt => checked(Damage + DealtOverkill);
    internal SourceDamage[] Sources(bool dealt) => DamageSources.Reconcile(dealt ? DealtSources : TakenSources,
        dealt ? Damage : DamageTaken - Overkill, dealt ? DealtOverkill : Overkill);
}
internal sealed record TeamStats(DamageRow[] Rows, long TotalDamage, long TotalKills, long TotalDamageTaken)
{
    internal long TotalDealt => checked(TotalDamage + Rows.Sum(row => row.DealtOverkill));
}
internal sealed record OverlaySnapshot(string LeftName, TeamStats Left, string RightName,
    TeamStats Right, string Caption, bool Available);

internal static class DamageModel
{
    private static DamageSample ExcludeSpellOverkill(DamageSample sample)
    {
        static long SpellExcess(SourceDamage[]? sources) => sources?
            .Where(s => s.Source.Category == DamageCategory.Spell).Sum(s => Math.Max(0, s.Overkill)) ?? 0;
        static SourceDamage[]? WithoutExcess(SourceDamage[]? sources) => sources?.Select(s =>
            s.Source.Category == DamageCategory.Spell ? s with { Overkill = 0 } : s).ToArray();
        var dealt = Math.Clamp(sample.Unit.Kind == 100 + (int)DamageCategory.Spell
            ? sample.DealtOverkill : SpellExcess(sample.DealtSources), 0, Math.Max(0, sample.DealtOverkill));
        var taken = Math.Clamp(SpellExcess(sample.TakenSources), 0,
            Math.Min(Math.Max(0, sample.Overkill), Math.Max(0, sample.DamageTaken)));
        if (dealt == 0 && taken == 0) return sample;
        // Native taken totals contain lethal excess; dealt is already effective
        // damage. Remove only the observed spell excess, before totals/graphs.
        return sample with { DealtOverkill = sample.DealtOverkill - dealt,
            DamageTaken = sample.DamageTaken - taken, Overkill = sample.Overkill - taken,
            DealtSources = WithoutExcess(sample.DealtSources), TakenSources = WithoutExcess(sample.TakenSources) };
    }

    internal static TeamStats Group(IEnumerable<DamageSample> samples)
    {
        var groups = new Dictionary<UnitKey, (long Damage, long Kills, long DamageTaken, long Overkill, long DealtOverkill)>();
        var sources = new Dictionary<(UnitKey, bool), List<SourceDamage>>();
        var hacks = new Dictionary<UnitKey, List<HackStats>>();
        var contributions = new Dictionary<UnitKey, List<Contribution>>();
        var casts = new Dictionary<UnitKey, long>();
        var squads = new Dictionary<UnitKey, List<SquadStats>>();
        foreach (var raw in samples)
        {
            var sample = ExcludeSpellOverkill(raw);
            if (!squads.TryGetValue(sample.Unit, out var squadList)) squads[sample.Unit] = squadList = new();
            // Apply the same spell-overkill policy to squad and type totals.
            squadList.AddRange((sample.Squads ?? Array.Empty<SquadStats>()).Select(s => {
                var adjusted = ExcludeSpellOverkill(new DamageSample(sample.Unit, s.Damage, s.Kills, s.Taken,
                    s.TakenOverkill, s.Overkill, s.DealtSources, s.TakenSources));
                return s with { Taken = adjusted.DamageTaken, TakenOverkill = adjusted.Overkill,
                    Overkill = adjusted.DealtOverkill, DealtSources = adjusted.DealtSources, TakenSources = adjusted.TakenSources };
            }));
            casts[sample.Unit] = checked(casts.GetValueOrDefault(sample.Unit) + Math.Max(0, sample.Casts));
            if (!hacks.TryGetValue(sample.Unit, out var hackList)) hacks[sample.Unit] = hackList = new();
            hackList.Add(sample.Hacking);
            if (!contributions.TryGetValue(sample.Unit, out var detailList)) contributions[sample.Unit] = detailList = new();
            detailList.AddRange(sample.Contributions ?? Array.Empty<Contribution>());
            foreach (var dealt in new[] { true, false })
            {
                var sourceKey = (sample.Unit, dealt);
                if (!sources.TryGetValue(sourceKey, out var list)) sources[sourceKey] = list = new();
                list.AddRange(DamageSources.Reconcile(dealt ? sample.DealtSources : sample.TakenSources,
                    dealt ? sample.Damage : sample.DamageTaken - Math.Clamp(sample.Overkill, 0, Math.Max(0, sample.DamageTaken)),
                    dealt ? sample.DealtOverkill : Math.Clamp(sample.Overkill, 0, Math.Max(0, sample.DamageTaken))));
            }
            var existing = groups.GetValueOrDefault(sample.Unit);
            groups[sample.Unit] = (
                checked(existing.Damage + Math.Max(0, sample.Damage)),
                checked(existing.Kills + Math.Max(0, sample.Kills)),
                checked(existing.DamageTaken + Math.Max(0, sample.DamageTaken)),
                checked(existing.Overkill + Math.Clamp(sample.Overkill, 0, Math.Max(0, sample.DamageTaken))),
                checked(existing.DealtOverkill + Math.Max(0, sample.DealtOverkill)));
        }

        var rows = groups.Select(pair => new DamageRow(pair.Key, pair.Value.Damage, pair.Value.Kills, pair.Value.DamageTaken, pair.Value.Overkill, pair.Value.DealtOverkill,
                DamageSources.Merge(sources[(pair.Key, true)]), DamageSources.Merge(sources[(pair.Key, false)])) {
                    Hacking = HackStats.Sum(hacks[pair.Key]), Contributions = Contribution.Merge(contributions[pair.Key]), Casts = casts[pair.Key], Squads = SquadStats.Merge(squads[pair.Key]) })
            .OrderByDescending(row => row.Damage + row.Hacking.Total)
            .ThenByDescending(row => row.Kills)
            .ThenBy(row => row.Unit.Kind)
            .ThenBy(row => row.Unit.Id)
            .ToArray();
        return new TeamStats(rows, rows.Sum(row => row.Damage), rows.Sum(row => row.Kills),
            rows.Sum(row => row.DamageTaken));
    }

    internal static float BarFraction(long damage, long maximum) => maximum <= 0
        ? 0f : (float)Math.Clamp((double)damage / maximum, 0d, 1d);
    internal static float BarFraction(double value, double maximum) => !double.IsFinite(value) || !double.IsFinite(maximum) || maximum <= 0
        ? 0f : (float)Math.Clamp(value / maximum, 0d, 1d);

}
