using BepInEx.Configuration;
using FixedMath;
using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using HarmonyLib;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Visual Helpers module. Own the preview outlines; never edit
// beacon commands, selection lists, materials or combat state.
internal static class BeaconSelectionPreview
{
    internal static ConfigEntry<bool> Enabled = null!;
    private static ReleaseCommanderSkillController? _controller;
    private static readonly List<Mech> Candidates = new();
    private static readonly Dictionary<long, BeaconOutline> Highlighted = new();
    private static float _nextUpdate;
    private static float _diagnosticAt;


    internal static void Bind(ConfigFile config) => Enabled = config.Bind("Features", "BeaconSelectionPreview", false,
        "RED - High competitive impact: targeting assistance. Disabled by default; keep off in competitive play. Outline units affected by the first beacon circle in white.");

    internal static void Clear()
    {
        foreach (var outline in Highlighted.Values) outline.Dispose();
        Highlighted.Clear(); Candidates.Clear(); _controller = null;
    }

    private static void Begin(ReleaseCommanderSkillController controller)
    {
        Clear();
        if (!Enabled.Value || controller.GetSkill()?.TryCast<CS_WayPoint>() is null) return;
        var scene = MatchClient.Current?.GetBattleScene();
        var player = scene?.GetInputController()?.GetPlayerController();
        var units = player?.GetUnitManager()?.units;
        var view = scene?.GetBattleView();
        if (units is null || view is null) return;
        for (var index = 0; index < units.Count; index++)
        {
            var mechs = view.GetMeches(units[index]);
            if (mechs is null) continue;
            for (var m = 0; m < mechs.Count; m++)
                if (mechs[m] != null)
                { Candidates.Add(mechs[m]); }
        }
        _controller = controller; _nextUpdate = 0; _diagnosticAt = 0;
        BattleStatisticsPlugin.Logger.LogInfo($"Beacon preview started: {Candidates.Count} friendly models cached.");
    }

    private static void Refresh(CommanderSkillReleaseView release)
    {
        if (_controller is null) return;
        if (!Enabled.Value || _controller.releaseAction?.TryCast<ReleaseCommanderSkillController.MultiReleaseAction>() is not { } action
            || action.currentCount != 0 || release.Pointer != _controller.commanderSkillReleaseView?.Pointer)
        { Clear(); return; }
        if (Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + .1f;
        using var timing = new SlowOperation("beacon selection preview");
        var input = MatchClient.Current?.GetBattleScene()?.GetInputController();
        if (input == null) { Clear(); return; }
        // GetReleasePosition can represent a stored waypoint. First-circle
        // preview must follow the native cursor's current map position.
        var selection = new CircleRange((FVector2)input.GetMousePositionOnMap(), _controller.GetSkill().GetSubEffectRange());
        var selected = 0;
        for (var index = 0; index < Candidates.Count; index++)
        {
            var mech = Candidates[index];
            if (mech == null) continue;
            var id = mech.Pointer.ToInt64();
            var actor = mech.GetSkillOwner();
            var inside = actor != null && actor.IsAlive() && selection.Overlaps(actor.GetBoundsCircle());
            if (inside) selected++;
            if (inside && !Highlighted.ContainsKey(id))
            {
                Highlighted.Add(id, new BeaconOutline(mech));
            }
            if (Highlighted.TryGetValue(id, out var outline)) outline.Refresh(inside);
        }
        if (Time.unscaledTime >= _diagnosticAt)
        {
            _diagnosticAt = Time.unscaledTime + 5;
            BattleStatisticsPlugin.Logger.LogInfo($"Beacon selection diagnostic: selected={selected}/{Candidates.Count}, center={FPoint.ToFloat(selection.center.x):0.##},{FPoint.ToFloat(selection.center.y):0.##}, radius={FPoint.ToFloat(selection.radius):0.##}.");
        }
    }

    [HarmonyPatch(typeof(ReleaseCommanderSkillController), nameof(ReleaseCommanderSkillController.Enter))]
    private static class Enter
    {
        private static void Postfix(ReleaseCommanderSkillController __instance)
        { try { Begin(__instance); } catch (Exception error) { Clear(); BattleStatisticsPlugin.Logger.LogWarning("Beacon preview unavailable: " + error.Message); } }
    }
    [HarmonyPatch(typeof(CommanderSkillReleaseView), nameof(CommanderSkillReleaseView.UpdatePosition))]
    private static class UpdatePosition
    {
        private static void Postfix(CommanderSkillReleaseView __instance)
        { try { Refresh(__instance); } catch (Exception error) { Clear(); BattleStatisticsPlugin.Logger.LogWarning("Beacon preview failed: " + error.Message); } }
    }
    [HarmonyPatch(typeof(ReleaseCommanderSkillController), nameof(ReleaseCommanderSkillController.Exit))]
    private static class Exit { private static void Prefix() => Clear(); }
    [HarmonyPatch(typeof(CommanderSkillReleaseView), nameof(CommanderSkillReleaseView.Hide))]
    private static class Hide { private static void Prefix() => Clear(); }
}
