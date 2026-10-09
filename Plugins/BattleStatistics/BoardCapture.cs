using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;


namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class BoardCapture
{
    private static BoardRect Rect(MapRect r) => new(r.x, r.y, r.width, r.height);
    private static BoardRect[] Cells(MapElement element)
    {
        var cells = element.GetActiveGrids();
        var result = new System.Collections.Generic.List<BoardRect>();
        if (cells is not null)
            for (var i = 0; i < cells.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<MapElement.MapElementGrid>>().Count; i++)
                if (cells[i].isActive) result.Add(Rect(cells[i].rect));
        if (result.Count == 0) result.Add(Rect(element.GetBound()));
        return result.ToArray();
    }

    internal static BoardSnapshot Capture(int round, int left, int right)
    {
        var regions = new System.Collections.Generic.List<BoardRect>();
        var pieces = new System.Collections.Generic.List<BoardPiece>();
        var players = MatchClient.Current?.GetPlayerManager()?.playerControllers;
        if (players is null) return new(round, regions.ToArray(), pieces.ToArray());
        for (var p = 0; p < players.Count; p++)
        {
            var player = players[p];
            var team = player.GetFightTeamController()?.GetTeamIndex();
            if (team != left && team != right) continue;
            var isRight = team == right;
            var territory = player.GetTerritoryManager()?.GetTerritory()?.GetRegions();
            if (territory is not null)
                for (var i = 0; i < territory.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<MapRegion>>().Count; i++)
                    regions.Add(Rect(territory[i].Bound));
            var manager = player.GetUnitManager();
            var units = manager.units;
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i]; var element = unit.GetMapElement();
                if (element is null) continue;
                pieces.Add(new($"deployed:{team}:{manager.GetUnitIndex(unit)}",
                    new((int)DamageRecorderType.Mech, unit.GetUnitData().GetMechID()), isRight, false, Cells(element)));
            }
            var buildings = player.GetBuildingManager()?.GetBuildings();
            if (buildings is not null)
                for (var i = 0; i < buildings.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<CrystalElement>>().Count; i++)
                {
                    var building = buildings[i]; var element = building.GetMapElement();
                    if (element is not null) pieces.Add(new($"tower:{team}:{i}",
                        new(-1, i), isRight, true, Cells(element)));
                }
            var constructions = player.GetConstructionManager()?.GetConstructionElements();
            if (constructions is not null)
                for (var i = 0; i < constructions.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ConstructionElement>>().Count; i++)
                {
                    var construction = constructions[i]; var element = construction.GetMapElement();
                    if (element is not null) pieces.Add(new($"construction:{team}:{construction.GetConstructionData().GetID()}",
                        new((int)DamageRecorderType.Construction, construction.GetConstructionData().GetID()), isRight, false, Cells(element)));
                }
        }
        return new(round, regions.Distinct().ToArray(), pieces.ToArray());
    }
}
