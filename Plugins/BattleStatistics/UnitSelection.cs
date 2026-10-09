namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class UnitSelection
{
    internal static int[] AvailableRounds(MatchHistory history, UnitCostHistory costs, BoardHistory boards, string id) =>
        history.Rounds.Where(r => r.Left.Rows.Concat(r.Right.Rows).Any(u => u.Squads?.Any(s => s.Id == id) == true)
            || costs.GetSquads(r.Number, false).ContainsKey(id) || costs.GetSquads(r.Number, true).ContainsKey(id)
            || boards.Get(r.Number)?.Pieces.Any(p => p.Id == id) == true).Select(r => r.Number).ToArray();
    internal static StatsTableRow[] Rows(IEnumerable<StatsTableRow> rows, string id, bool? preferredRight = null)
    {
        var ordered = rows.ToArray();
        bool Match(StatsTableRow r) => r.Id == id || r.MapIds.Contains(id);
        var index = Array.FindIndex(ordered, r => Match(r) && (preferredRight is null || r.Right == preferredRight));
        if (index < 0) index = Array.FindIndex(ordered, Match);
        if (index < 0) return Array.Empty<StatsTableRow>();
        var root = ordered[index];
        return ordered.Skip(index).Take(1).Concat(ordered.Skip(index + 1).TakeWhile(r => r.Depth > root.Depth))
            .Select(r => r with { Depth = r.Depth - root.Depth }).ToArray();
    }
}
