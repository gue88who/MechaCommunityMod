namespace MechaCommunityMod.Plugins.BattleStatistics;

internal enum UnitCostKind { Purchase, Upgrade, Technology }
internal readonly record struct UnitCost(long Purchase, long Upgrade, long Technology)
{
    internal long Total => checked(Purchase + Upgrade + Technology);
    internal static Dictionary<UnitKey, UnitCost> Deployment(IEnumerable<(UnitKey Unit, long Base, long Upgrades, long Tech)> squads)
        => squads.GroupBy(s => s.Unit).ToDictionary(g => g.Key,
            g => new UnitCost(g.Sum(s => s.Base), g.Sum(s => s.Upgrades), g.Max(s => s.Tech)));
    internal UnitCost Add(UnitCostKind kind, long amount) => kind switch
    {
        UnitCostKind.Purchase => this with { Purchase = checked(Purchase + amount) },
        UnitCostKind.Upgrade => this with { Upgrade = checked(Upgrade + amount) },
        _ => this with { Technology = checked(Technology + amount) }
    };
}

// Credits spent, not repeated per-round army valuations. Undo removes an action;
// redo replaces it. A shared technology is one transaction for its unit type.
internal sealed class UnitCostLedger
{
    private readonly Dictionary<(int Round, int Team, string Action), (int Round, UnitKey Unit, UnitCostKind Kind, long Amount)> _actions = new();
    private int _round = -1;
    internal void ObserveRound(int round)
    {
        if (round < _round)
            foreach (var key in _actions.Where(p => p.Value.Round >= round).Select(p => p.Key).ToArray()) _actions.Remove(key);
        _round = round;
    }
    internal void Record(int round, int team, string action, UnitKey unit, UnitCostKind kind, long amount)
    {
        ObserveRound(round);
        _actions[(round, team, action)] = (round, unit, kind, Math.Max(0, amount));
    }
    internal void Undo(int team, string action) => _actions.Remove((_round, team, action));
    internal Dictionary<UnitKey, UnitCost> Snapshot(int team)
    {
        var result = new Dictionary<UnitKey, UnitCost>();
        foreach (var entry in _actions.Where(p => p.Key.Team == team).Select(p => p.Value))
            result[entry.Unit] = result.GetValueOrDefault(entry.Unit).Add(entry.Kind, entry.Amount);
        return result;
    }
}

internal sealed class UnitCostHistory
{
    private readonly SortedDictionary<int, (Dictionary<UnitKey, UnitCost> Left, Dictionary<UnitKey, UnitCost> Right)> _rounds = new();
    private MatchIdentity? _identity;
    private readonly SortedDictionary<int, (Dictionary<string, UnitCost> Left, Dictionary<string, UnitCost> Right)> _squads = new();
    internal void Begin(MatchIdentity identity)
    {
        if (_identity == identity) return;
        _identity = identity;
        _rounds.Clear();
        _squads.Clear();
    }
    internal bool Has(int round) => _rounds.ContainsKey(round);
    internal void Resume(int completed)
    {
        foreach (var round in _rounds.Keys.Where(r => r > completed).ToArray()) _rounds.Remove(round);
        foreach (var round in _squads.Keys.Where(r => r > completed).ToArray()) _squads.Remove(round);
    }
    internal void Put(int round, Dictionary<UnitKey, UnitCost> left, Dictionary<UnitKey, UnitCost> right,
        Dictionary<string, UnitCost>? leftSquads = null, Dictionary<string, UnitCost>? rightSquads = null)
    {
        _rounds[round] = (new(left), new(right));
        _squads[round] = (new(leftSquads ?? new()), new(rightSquads ?? new()));
    }
    internal IReadOnlyDictionary<string, UnitCost> GetSquads(int round, bool right)
    {
        if (round != 0) return _squads.TryGetValue(round, out var entry) ? (right ? entry.Right : entry.Left) : new Dictionary<string, UnitCost>();
        var latest = new Dictionary<string, UnitCost>();
        foreach (var entry in _squads.Values)
            foreach (var pair in right ? entry.Right : entry.Left) latest[pair.Key] = pair.Value;
        return latest;
    }
    internal IReadOnlyDictionary<UnitKey, UnitCost>? Get(int round, bool right)
    {
        if (round == 0) round = _rounds.Count == 0 ? -1 : _rounds.Keys.Last();
        return _rounds.TryGetValue(round, out var entry) ? (right ? entry.Right : entry.Left) : null;
    }
}
