using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class CostHooks
{
    private static readonly Dictionary<int, Dictionary<string, UnitCost>> Individual = new();
    internal static Dictionary<string, UnitCost> Squads(int team) => Individual.GetValueOrDefault(team) ?? new();
    internal static Dictionary<UnitKey, UnitCost> Snapshot(int team)
    {
        var match = MatchClient.Current;
        var samples = new List<(UnitKey Unit, long Base, long Upgrades, long Tech)>();
        var individual = new Dictionary<string, UnitCost>();
        Individual[team] = individual;
        var players = match?.GetPlayerManager()?.playerControllers;
        if (players is null) return new();
        for (var p = 0; p < players.Count; p++)
        {
            var player = players[p];
            if (player.GetFightTeamController()?.GetTeamIndex() != team) continue;
            var units = player.GetUnitManager().units;
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                var data = unit.GetUnitData();
                var techs = player.GetTechnologyManager().GetTechnologyManager(unit.GetID());
                // Value gifted squads too. The native sell value contains level
                // investment plus the saved purchase price; replace the latter
                // with the catalog base value instead of treating gifts as free.
                samples.Add((new UnitKey((int)DamageRecorderType.Mech, data.GetMechID()),
                    Math.Max(0, data.GetBaseSupply()), Math.Max(0L, (long)unit.GetSellSupply() - unit.GetBuySupply()),
                    techs is null ? 0 : Math.Max(0, UnitUtility.CalculateUpgradeTechnologyCost(unit.GetID(), techs.technologies))));
                individual[$"deployed:{team}:{player.GetUnitManager().GetUnitIndex(unit)}"] = new(
                    Math.Max(0, data.GetBaseSupply()), Math.Max(0L, (long)unit.GetSellSupply() - unit.GetBuySupply()), 0);
            }
        }
        return UnitCost.Deployment(samples);
    }
}
