namespace MechaCommunityMod.Plugins.BattleStatistics;

internal readonly record struct MatchIdentity(long Match, long Statistics, int StartedAt);
internal sealed record RoundRecord(int Number, TeamStats Left, TeamStats Right, bool Complete, RoundInfo? Info = null);
internal enum ResultMetric { Damage, Tanked, Kills, XpEarned, EnemyXpAwarded }

// Only managed copies survive round cleanup. Each round is replaced, never accumulated
// from polling samples. No game objects or native pointers are dereferenced here.
internal sealed class MatchHistory
{
    private readonly SortedDictionary<int, RoundRecord> _rounds = new();
    private MatchIdentity? _identity;
    private int _archiveCount;
    internal int Session { get; private set; }
    internal int Revision { get; private set; }
    internal Guid ArchiveId { get; private set; }
    internal DateTimeOffset StartedUtc { get; private set; }
    internal int Timeline { get; private set; }
    internal int CompletedCount => _rounds.Values.Count(r => r.Complete);
    internal string LeftName { get; private set; } = "Left army";
    internal string RightName { get; private set; } = "Right army";
    internal bool Ended { get; private set; }
    internal bool Finished { get; private set; }
    internal string? Outcome { get; private set; }
    internal IReadOnlyList<RoundRecord> Rounds => _rounds.Values.ToArray();
    internal int Count => _rounds.Count;
    internal bool HasPartialRound => _rounds.Values.Any(r => !r.Complete);

    internal void Begin(MatchIdentity identity, string leftName, string rightName)
    {
        if (_identity != identity)
        {
            _identity = identity;
            ArchiveId = Guid.NewGuid();
            StartedUtc = DateTimeOffset.UtcNow;
            Timeline = 0;
            _rounds.Clear();
            _archiveCount = 0;
            Ended = Finished = false;
            Outcome = null;
            Session++;
            Revision++;
        }
        LeftName = leftName;
        RightName = rightName;
    }

    internal bool NeedsArchiveRound(int number, int archiveCount) =>
        number == archiveCount || !_rounds.TryGetValue(number, out var row) || !row.Complete;

    internal bool SyncArchiveCount(int count)
    {
        // Native cleanup after game over is not a replay rewind.
        if (Ended && count < _archiveCount) return false;
        if (count < _archiveCount)
        {
            Timeline++;
            // A rewind/retry invalidates the future, including any sampled live round.
            foreach (var number in _rounds.Keys.Where(n => n > count).ToArray())
                _rounds.Remove(number);
            Ended = Finished = false;
            Outcome = null;
            Revision++;
        }
        _archiveCount = count;
        return true;
    }

    internal void Resume(int completedRounds)
    {
        if (Ended || _rounds.Keys.Any(r => r > completedRounds)) Timeline++;
        // Called on a real fight start, also covering retries with an unchanged archive size.
        foreach (var number in _rounds.Keys.Where(n => n > completedRounds).ToArray())
            _rounds.Remove(number);
        _archiveCount = completedRounds;
        Ended = Finished = false;
        Outcome = null;
        Revision++;
    }

    internal void Put(int number, TeamStats left, TeamStats right, bool complete)
    {
        if (number < 1) return;
        if (!complete && _rounds.TryGetValue(number, out var old) && old.Complete) return;
        // Copy arrays as well, so later presentation or native refreshes cannot mutate history.
        _rounds[number] = new RoundRecord(number, Copy(left), Copy(right), complete, _rounds.GetValueOrDefault(number)?.Info);
        Revision++;
    }
    internal void SetRoundInfo(int number, RoundInfo info)
    {
        if (!_rounds.TryGetValue(number, out var round) || round.Info == info) return;
        _rounds[number] = round with { Info = info }; Revision++;
    }

    internal void End(bool finished, string? outcome = null)
    {
        if (_identity is null) return;
        var changed = !Ended || (finished && !Finished) || (outcome is not null && Outcome != outcome);
        Ended = true;
        Finished |= finished;
        if (outcome is not null) Outcome = outcome;
        if (changed) Revision++;
    }

    internal RoundRecord? Find(int number) => _rounds.GetValueOrDefault(number);

    internal RoundRecord Overall() => new(0,
        Combine(_rounds.Values.Select(r => r.Left)),
        Combine(_rounds.Values.Select(r => r.Right)), !HasPartialRound);

    internal static long Value(TeamStats team, ResultMetric metric) => metric switch
    {
        ResultMetric.Tanked => team.TotalDamageTaken,
        ResultMetric.Kills => team.TotalKills,
        _ => team.TotalDealt
    };

    internal static long Value(DamageRow row, ResultMetric metric) => metric switch
    {
        ResultMetric.Tanked => row.DamageTaken,
        ResultMetric.Kills => row.Kills,
        _ => row.TotalDealt
    };

    internal (int Round, long Left, long Right)[] Series(ResultMetric metric, bool cumulative) =>
        Series(team => Value(team, metric), cumulative);

    internal static double MetricValue(TeamStats team, ResultMetric metric) => metric switch {
        ResultMetric.XpEarned => team.Rows.Sum(r => r.XpEarned),
        ResultMetric.EnemyXpAwarded => team.Rows.Sum(r => r.EnemyXpAwarded), _ => Value(team, metric) };
    internal static double MetricValue(DamageRow row, ResultMetric metric) => metric switch {
        ResultMetric.XpEarned => row.XpEarned, ResultMetric.EnemyXpAwarded => row.EnemyXpAwarded, _ => Value(row, metric) };
    internal (int Round, double Left, double Right)[] MetricSeries(ResultMetric metric, bool cumulative)
    {
        double left = 0, right = 0;
        return _rounds.Values.Select(r => {
            left = cumulative ? left + MetricValue(r.Left, metric) : MetricValue(r.Left, metric);
            right = cumulative ? right + MetricValue(r.Right, metric) : MetricValue(r.Right, metric);
            return (r.Number, left, right);
        }).ToArray();
    }

    internal (int Round, long Left, long Right)[] OverkillSeries(bool cumulative, bool dealt = false) =>
        Series(team => team.Rows.Sum(row => dealt ? row.DealtOverkill : row.Overkill), cumulative);

    private (int Round, long Left, long Right)[] Series(Func<TeamStats, long> value, bool cumulative)
    {
        long left = 0, right = 0;
        return _rounds.Values.Select(r =>
        {
            left = cumulative ? checked(left + value(r.Left)) : value(r.Left);
            right = cumulative ? checked(right + value(r.Right)) : value(r.Right);
            return (r.Number, left, right);
        }).ToArray();
    }

    private static TeamStats Copy(TeamStats team) => team with { Rows = team.Rows.Select(r => r with {
        DealtSources = r.DealtSources?.ToArray(), TakenSources = r.TakenSources?.ToArray(), Contributions = r.Contributions?.ToArray(),
        Squads = r.Squads?.Select(s => s with { DealtSources = s.DealtSources?.ToArray(), TakenSources = s.TakenSources?.ToArray(),
            Levels = s.Levels?.ToArray() }).ToArray() }).ToArray() };
    private static TeamStats Combine(IEnumerable<TeamStats> teams) => DamageModel.Group(
        teams.SelectMany(t => t.Rows).Select(r => new DamageSample(r.Unit, r.Damage, r.Kills, r.DamageTaken, r.Overkill, r.DealtOverkill, r.DealtSources, r.TakenSources, r.Hacking, r.Contributions, r.Casts, r.Squads)));
}
