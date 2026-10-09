namespace MechaCommunityMod.Plugins.BattleStatistics;

// XP stored by the game for this level, not XP gained during the round.
internal sealed record UnitLevelSnapshot(int Round, int Level, double Experience,
    double NextLevelExperience, bool MaxLevel)
{
    internal double? Progress => MaxLevel ? null : double.IsFinite(Experience)
        && double.IsFinite(NextLevelExperience) && NextLevelExperience > 0
            ? Math.Clamp(Experience / NextLevelExperience, 0, 1) : null;
    internal static UnitLevelSnapshot[] Merge(IEnumerable<UnitLevelSnapshot> values) => values
        .Where(v => v.Round > 0 && v.Level > 0).GroupBy(v => v.Round)
        .Select(g => g.Last()).OrderBy(v => v.Round).ToArray();
}

internal sealed class UnitLevelLedger
{
    private readonly Dictionary<(long Round, int Team, string Id), UnitLevelSnapshot> _values = new();
    internal void Put(long round, int team, string id, UnitLevelSnapshot value) => _values[(round, team, id)] = value;
    internal UnitLevelSnapshot[] Get(long round, int team, string id) =>
        _values.TryGetValue((round, team, id), out var value) ? new[] { value } : Array.Empty<UnitLevelSnapshot>();
    internal void Clear() => _values.Clear();
    internal void Retain(IEnumerable<long> rounds)
    {
        var keep = rounds.ToHashSet();
        foreach (var key in _values.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) _values.Remove(key);
    }
}

internal sealed record UnitLevelBadge(string Label, double? Progress, string Hint)
{
    internal static UnitLevelBadge? From(IEnumerable<SquadStats> squads)
    {
        var own = squads.GroupBy(s => s.Id).Select(g => g.Last()).ToArray();
        if (own.Length == 0) return null;
        var current = own.Select(s => s.Levels?.OrderBy(v => v.Round).LastOrDefault()).ToArray();
        var known = current.Where(v => v is not null).Select(v => v!).ToArray();
        if (known.Length == 0) return null;
        var min = known.Min(v => v.Level); var max = known.Max(v => v.Level);
        var label = known.Length != own.Length ? "?" : min == max ? min.ToString() : $"{min}–{max}";
        double? progress = known.Length == own.Length && min == max && known.All(v => v.Progress is not null)
            ? known.Sum(v => Math.Clamp(v.Experience, 0, v.NextLevelExperience)) / known.Sum(v => v.NextLevelExperience) : null;
        string State(UnitLevelSnapshot v) => v.MaxLevel ? $"Lv {v.Level} · MAX"
            : $"Lv {v.Level} · {v.Experience:N1} / {v.NextLevelExperience:N1} XP";
        var hint = own.Length == 1 ? string.Join("\n", (own[0].Levels ?? Array.Empty<UnitLevelSnapshot>())
                .OrderBy(v => v.Round).Select(v => $"R{v.Round}: {State(v)}"))
            : string.Join("\n", known.GroupBy(v => v.Level).OrderBy(g => g.Key)
                .Select(g => $"Lv {g.Key} ×{g.Count()}"))
                + (known.Length == own.Length ? "" : $"\nUnobserved ×{own.Length - known.Length}")
                + (progress is { } fill ? $"\nCombined XP: {fill:P0}" : "");
        return new(label, progress, hint);
    }
}

internal static class UnitLevelArc
{
    internal static int Key(double progress) => double.IsFinite(progress)
        ? (int)Math.Round(Math.Clamp(progress, 0, 1) * 360) : 0;

    // A single angular alpha mask over native artwork. Sample each source pixel
    // at four positions to soften the moving edge, without separate GUI clips.
    // Coordinates use the GUI's top-left origin, clockwise from twelve o'clock.
    internal static double Coverage(double progress, double x, double y, double pixelWidth, double pixelHeight)
    {
        if (!double.IsFinite(progress) || progress <= 0) return 0;
        if (progress >= 1) return 1;
        var end = progress * Math.PI * 2;
        var covered = 0;
        for (var sampleY = -1; sampleY <= 1; sampleY += 2)
            for (var sampleX = -1; sampleX <= 1; sampleX += 2)
            {
                var angle = Math.Atan2(x + sampleX * .25 * pixelWidth - .5, .5 - y - sampleY * .25 * pixelHeight);
                if (angle < 0) angle += Math.PI * 2;
                if (angle < end) covered++;
            }
        return covered / 4d;
    }
}
