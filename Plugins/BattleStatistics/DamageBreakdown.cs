namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed record DamageDetail(DamageSource Source, bool Category, long Damage, long Overkill);

internal static class DamageBreakdown
{
    internal static DamageDetail[] Rows(DamageRow row, bool dealt)
    {
        var result = new List<DamageDetail>();
        // Incoming technologies describe the attacker, never the victim's loadout.
        // Keep channels separate rather than unioning their names in one list.
        foreach (var group in row.Sources(dealt).Where(s => s.Total > 0)
            .GroupBy(s => s.Source.Category).OrderBy(g => g.Key))
        {
            var sources = group.OrderByDescending(s => s.Total).ThenBy(s => s.Source.Name).ToArray();
            result.Add(new DamageDetail(new DamageSource(group.Key, 0, ""), true,
                sources.Sum(s => s.Damage), sources.Sum(s => s.Overkill)));
            if (group.Key is DamageCategory.Tech or DamageCategory.Spell or DamageCategory.Other)
                foreach (var source in sources)
                    result.Add(new DamageDetail(source.Source, false, source.Damage, source.Overkill));
        }
        return result.ToArray();
    }
}
