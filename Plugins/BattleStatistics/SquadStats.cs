namespace MechaCommunityMod.Plugins.BattleStatistics;

// Match-local identifiers and detached values; never serialize native pointers.
internal sealed record SquadStats(string Id, string Name, long Damage = 0, long Kills = 0,
    long Taken = 0, long Overkill = 0, long TakenOverkill = 0, double XpEarned = 0,
    double EnemyXpAwarded = 0, SourceDamage[]? DealtSources = null, SourceDamage[]? TakenSources = null,
    string? ParentId = null, UnitKey? ParentUnit = null, bool Spawned = false, long? Hacked = null, long? CoreDamage = null,
    HackStats? Hacking = null, UnitLevelSnapshot[]? Levels = null)
{
    internal static SquadStats[] Merge(IEnumerable<SquadStats> values) => values.Select(s => s with {
            DealtSources = DamageSources.Reconcile(s.DealtSources, s.Damage, s.Overkill),
            TakenSources = DamageSources.Reconcile(s.TakenSources, s.Taken - s.TakenOverkill, s.TakenOverkill) })
        .GroupBy(s => s.Id)
        .Select(g => g.First() with { Damage = g.Sum(s => s.Damage), Kills = g.Sum(s => s.Kills),
            Taken = g.Sum(s => s.Taken), Overkill = g.Sum(s => s.Overkill), TakenOverkill = g.Sum(s => s.TakenOverkill),
            XpEarned = g.Sum(s => s.XpEarned), EnemyXpAwarded = g.Sum(s => s.EnemyXpAwarded),
            Hacked = g.Any(s => s.Hacked is not null) ? g.Sum(s => s.Hacked ?? 0) : null,
            Levels = UnitLevelSnapshot.Merge(g.SelectMany(s => s.Levels ?? Array.Empty<UnitLevelSnapshot>())),
            Hacking = g.Any(s => s.Hacking is null) ? null : HackStats.Sum(g.Select(s => s.Hacking!.Value)),
            CoreDamage = g.Any(s => s.CoreDamage is null) ? null : g.Sum(s => s.CoreDamage ?? 0),
            DealtSources = DamageSources.Merge(g.SelectMany(s => s.DealtSources ?? Array.Empty<SourceDamage>())),
            TakenSources = DamageSources.Merge(g.SelectMany(s => s.TakenSources ?? Array.Empty<SourceDamage>())) })
        .OrderByDescending(s => s.Damage + s.Overkill).ThenBy(s => s.Id).ToArray();
}

internal static class SquadNames
{
    private static readonly string[] Names = ("Gary Bob Amanda Alice Ben Charlotte Daniel Emma Finn Grace Henry " +
        "Isabel Jack Kate Liam Mia Noah Olivia Paul Quinn Rose Sam Theo Uma Victor Wendy Xavier Yasmin Zoe " +
        "Adam Bella Charlie Daisy Ethan Freya George Hannah Isaac Julia Leo Lucy Max Nora Oscar Penny Riley " +
        "Sophie Thomas Violet William Alex Amy Arthur Chloe David Ella Felix Holly James Lily Logan Molly " +
        "Nathan Ruby Sarah Simon Toby Aaron Clara Dylan Eva Hugo Ivy Jacob Laura Luke Oliver Peter Rachel " +
        "Ryan Sebastian Stella Tom Abigail Adrian Andrew Anna Caleb Caroline Edward Emily Eric Florence " +
        "Gabriel Hazel Jessica Joseph Louis Madison Matthew Megan Miles Naomi Nicholas Patrick Rebecca " +
        "Robert Scott Sean Victoria").Split(' ');
    internal static string At(int index) => Names[index % Names.Length] + (index < Names.Length ? "" : " " + (index / Names.Length + 1));
    internal static string Label(string type, string name, bool squad) => squad ? $"{name}'s {type} squad" : $"{type} {name}";
}

internal static class SquadPresentation
{
    internal static TeamStats Split(TeamStats team, bool hacking, Func<UnitKey, string>? typeName = null)
    {
        IEnumerable<DamageRow> Rows(DamageRow row)
        {
            var squads = row.Squads;
            if (squads is not { Length: > 0 }) { yield return row; yield break; }
            var damage = squads.Sum(s => s.Damage); var taken = squads.Sum(s => s.Taken);
            var excess = squads.Sum(s => s.Overkill); var takenExcess = squads.Sum(s => s.TakenOverkill);
            var kills = squads.Sum(s => s.Kills);
            // Partial observation must never hide damage or inflate the live list.
            if (damage > row.Damage || taken > row.DamageTaken || excess > row.DealtOverkill || takenExcess > row.Overkill || kills > row.Kills)
            { yield return row; yield break; }
            foreach (var s in squads)
                yield return new(row.Unit, s.Damage, s.Kills, s.Taken, s.TakenOverkill, s.Overkill, s.DealtSources, s.TakenSources)
                    { DisplayName = s.Name, Squads = new[] { s } };
            if (damage < row.Damage || taken < row.DamageTaken || excess < row.DealtOverkill || takenExcess < row.Overkill || kills < row.Kills)
                yield return new(row.Unit, row.Damage - damage, row.Kills - kills, row.DamageTaken - taken,
                    row.Overkill - takenExcess, row.DealtOverkill - excess) { DisplayName = "Unassigned " + (typeName?.Invoke(row.Unit) ?? "unit") };
            if (hacking && (row.Hacking.Total > 0 || row.Hacking.Converted > 0))
                yield return new(row.Unit, 0, 0, 0) { Hacking = row.Hacking, DisplayName = (typeName?.Invoke(row.Unit) ?? "Type") + " hacking totals" };
        }
        return team with { Rows = team.Rows.SelectMany(Rows).OrderByDescending(r => r.TotalDealt + r.Hacking.Total).ThenBy(r => r.DisplayName).ToArray() };
    }
}

internal sealed class ExperienceLedger
{
    private readonly Dictionary<(long Round, int Team, string Squad), (double Earned, double Awarded)> _values = new();
    internal void Add(long round, int team, string squad, double earned, double awarded)
    {
        var key = (round, team, squad); var old = _values.GetValueOrDefault(key);
        _values[key] = (old.Earned + Valid(earned), old.Awarded + Valid(awarded));
    }
    private static double Valid(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    internal (double Earned, double Awarded) Get(long round, int team, string squad) => _values.GetValueOrDefault((round, team, squad));
    internal void Clear() => _values.Clear();
    internal void Retain(IEnumerable<long> rounds)
    {
        var keep = rounds.ToHashSet();
        foreach (var key in _values.Keys.Where(k => !keep.Contains(k.Round)).ToArray()) _values.Remove(key);
    }
}
