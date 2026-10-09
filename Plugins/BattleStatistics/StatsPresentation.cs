namespace MechaCommunityMod.Plugins.BattleStatistics;

internal enum StatsScope { All, Damage, Tanking, Experience, Kills }
internal static class StatsPresentation
{
    internal static OverlaySnapshot? CompletedBattle(MatchHistory history, bool battlefieldPresent)
    {
        if (!battlefieldPresent || !history.Ended || history.Count == 0) return null;
        var last = history.Rounds.Last();
        return new(history.LeftName, last.Left, history.RightName, last.Right, "LAST FIGHT", true);
    }
    internal static bool Damage(StatsScope scope, bool enabled) => enabled && scope is StatsScope.All or StatsScope.Damage;
    internal static bool Tanking(StatsScope scope, bool enabled) => enabled && scope is StatsScope.All or StatsScope.Tanking;
    internal static bool Experience(StatsScope scope, bool enabled) => enabled && scope is StatsScope.All or StatsScope.Experience;
    internal static bool Kills(StatsScope scope, bool enabled) => enabled && scope is StatsScope.All or StatsScope.Kills;
    internal static ResultMetric Metric(StatsScope scope) => scope switch {
        StatsScope.Tanking => ResultMetric.Tanked, StatsScope.Experience => ResultMetric.XpEarned,
        StatsScope.Kills => ResultMetric.Kills, _ => ResultMetric.Damage };
}
