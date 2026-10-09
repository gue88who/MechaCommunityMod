namespace MechaCommunityMod.Plugins.BattleStatistics;

internal enum RoundWinner { Unknown, Blue, Red, Draw }
internal sealed record RoundInfo(RoundWinner Winner, long? LeftHpDamage, long? RightHpDamage,
    long? LeftHpBefore = null, long? LeftHpAfter = null, long? RightHpBefore = null, long? RightHpAfter = null,
    long? LeftUnattributedCoreDamage = null, long? RightUnattributedCoreDamage = null);

internal sealed class PlayerHpChanges
{
    private readonly Dictionary<int, (long Loss, long After)> _values = new();
    internal void Observe(int team, long before, long after)
    {
        var loss = checked(_values.GetValueOrDefault(team).Loss + Math.Max(0, before - after));
        _values[team] = (loss, after);
    }
    internal long? Loss(int team) => _values.TryGetValue(team, out var value) ? value.Loss : null;
    internal long? After(int team) => _values.TryGetValue(team, out var value) ? value.After : null;
    internal void Clear() => _values.Clear();
}

internal static class CoreDamageAttribution
{
    // Native survivor scores are usable as HP damage only when they reconcile
    // exactly with the observed opponent HP loss. Never invent a proportional split.
    internal static Dictionary<string, long>? Attribute(IEnumerable<(string Id, long Score)> values, long damage)
    {
        var rows = values.ToArray();
        if (damage < 0 || rows.Any(r => r.Score < 0) || rows.Sum(r => r.Score) != damage) return null;
        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Sum(r => r.Score));
    }
}
