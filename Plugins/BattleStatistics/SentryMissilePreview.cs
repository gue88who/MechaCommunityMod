using BepInEx.Configuration;
using FixedMath;
using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using HarmonyLib;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Read-only placement preview; shares the beacon's silhouette renderer.
internal static class SentryMissilePreview
{
    internal static ConfigEntry<bool> Enabled = null!;
    private static ReleaseContraptionController? _controller;
    private static BattleView? _view;
    private readonly record struct Candidate(SceneActor View, FightActor Actor, bool IsConstruction, bool IsTower);
    private static readonly List<Candidate> Candidates = new();
    private static readonly HashSet<int> OpponentTeams = new();
    private static readonly Dictionary<long, BeaconOutline> Highlighted = new();
    private static readonly HashSet<long> SelectedModels = new();
    private static int _fixedCandidateCount;
    private static float _nextUpdate, _diagnosticAt;

    internal static void Bind(ConfigFile config) => Enabled = config.Bind("Features", "SentryMissilePreview", false,
        "RED - High competitive impact: targeting assistance. Disabled by default; keep off in competitive play. Outline eligible enemy units and buildings within a sentry missile's trigger range while placing it.");

    internal static void Clear()
    {
        foreach (var outline in Highlighted.Values) outline.Dispose();
        Highlighted.Clear(); Candidates.Clear(); OpponentTeams.Clear(); SelectedModels.Clear();
        _controller = null; _view = null; _fixedCandidateCount = 0;
    }

    private static void Begin(ReleaseContraptionController controller)
    {
        Clear();
        if (!Enabled.Value || controller.contraption?.TryCast<LandMineContraption>() is null) return;
        var scene = MatchClient.Current?.GetBattleScene();
        var player = scene?.GetInputController()?.GetPlayerController();
        var opponents = player?.GetOpponentController()?.opponentPlayerControllers;
        var view = scene?.GetBattleView();
        if (opponents is null || view is null) return;
        var seen = new HashSet<IntPtr>();
        for (var p = 0; p < opponents.Count; p++)
        {
            if (opponents[p] == null) continue;
            OpponentTeams.Add(opponents[p].GetTeamIndex());
            var units = opponents[p]?.GetUnitManager()?.units;
            if (units is null) continue;
            for (var u = 0; u < units.Count; u++)
            {
                var mechs = view.GetMeches(units[u]);
                if (mechs is null) continue;
                for (var m = 0; m < mechs.Count; m++)
                    if (mechs[m] is { } mech && mech.GetFightActor() is { } actor && seen.Add(mech.Pointer))
                        Candidates.Add(new(mech, actor, false, false));
            }
        }
        // Crystals/interceptors are absent from players' squad lists. Read
        // constructed buildings separately from their active view manager below.
        var buildings = 0;
        var actors = view.GetSceneActors();
        if (actors != null)
            foreach (var entry in actors)
            {
                var actor = entry.Key; var model = entry.Value;
                if (actor == null || model == null || !OpponentTeams.Contains(actor.GetCurrentTeamController()?.GetTeamIndex() ?? -1)) continue;
                if (actor.TryCast<FightConstruction>() != null) continue;
                var crystal = actor.TryCast<FightCrystal>();
                if (crystal == null || !seen.Add(model.Pointer)) continue;
                Candidates.Add(new(model, actor, false, crystal.IsTower()));
                buildings++;
            }
        _view = view; _fixedCandidateCount = Candidates.Count;
        buildings += RefreshConstructions();
        _controller = controller; _nextUpdate = _diagnosticAt = 0;
        BattleStatisticsPlugin.Logger.LogInfo($"Sentry missile preview started: {Candidates.Count} enemy models cached, including {buildings} buildings.");
    }

    private static int RefreshConstructions()
    {
        // Walls and turrets retain scene views across rounds while their fight
        // bindings can be reset/replaced. Do not freeze a registry key at Enter.
        Candidates.RemoveRange(_fixedCandidateCount, Candidates.Count - _fixedCandidateCount);
        var models = _view?.GetConstructionBuildings();
        if (models == null) return 0;
        var modelCount = models.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ConstructionBuilding>>().Count;
        var count = 0;
        for (var index = 0; index < modelCount; index++)
        {
            var model = models[index];
            var actor = model?.GetFightConstruction();
            if (model == null || actor == null || !OpponentTeams.Contains(actor.GetCurrentTeamController()?.GetTeamIndex() ?? -1)) continue;
            Candidates.Add(new(model, actor, true, actor.IsTower()));
            count++;
        }
        return count;
    }

    private static void Refresh(CommanderSkillReleaseView release)
    {
        if (_controller is null) return;
        if (!Enabled.Value || release.Pointer != _controller.commanderSkillReleaseView?.Pointer)
        { Clear(); return; }
        if (Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + .1f;
        using var timing = new SlowOperation("sentry missile selection preview");
        var constructions = RefreshConstructions();
        SelectedModels.Clear();
        // Follow the native, snapped placement position and the trigger radius,
        // not the mouse's raw map position or the explosion's splash radius.
        var center = (FVector2)_controller.GetReleasePosition();
        var radius = _controller.contraption.GetRange();
        var canRelease = _controller.CanRelease();
        var canAttackConstruction = _controller.contraption.TryCast<LandMineContraption>()?.CanAttackConstruction() == true;
        var selected = 0; var selectedConstructions = 0;
        foreach (var candidate in Candidates)
        {
            var model = candidate.View; var actor = candidate.Actor;
            if (model == null) continue;
            var id = model.Pointer.ToInt64();
            // TeamMineManager.GetNearestTarget (shared by TryActiveMine and the
            // native deployment arrow) uses distance-to-center minus the
            // actor's gameplay radius, with a strict comparison at the edge.
            var inside = canRelease && actor != null && actor.IsAlive()
                && OpponentTeams.Contains(actor.GetCurrentTeamController()?.GetTeamIndex() ?? -1)
                && !candidate.IsTower && (!candidate.IsConstruction || canAttackConstruction)
                && actor.GetVisibility() is ActorVisibility.Normal or ActorVisibility.Disappear
                && FightUtility.CalculateDistance2D(center, actor.GetFightTransform()) - actor.GetRadius() < radius;
            if (inside)
            {
                selected++;
                if (candidate.IsConstruction) selectedConstructions++;
                SelectedModels.Add(id);
                if (!Highlighted.ContainsKey(id)) Highlighted.Add(id, new BeaconOutline(model));
            }
        }
        // Also hide outlines for buildings removed or rebound during placement.
        foreach (var entry in Highlighted) entry.Value.Refresh(SelectedModels.Contains(entry.Key));
        if (Time.unscaledTime >= _diagnosticAt)
        {
            _diagnosticAt = Time.unscaledTime + 5;
            BattleStatisticsPlugin.Logger.LogInfo($"Sentry missile selection diagnostic: selected={selected}/{Candidates.Count}, constructions={selectedConstructions}/{constructions}, canAttackConstruction={canAttackConstruction}, center={FPoint.ToFloat(center.x):0.##},{FPoint.ToFloat(center.y):0.##}, radius={FPoint.ToFloat(radius):0.##}, canPlace={canRelease}.");
        }
    }

    [HarmonyPatch(typeof(ReleaseContraptionController), nameof(ReleaseContraptionController.Enter))]
    private static class Enter
    {
        private static void Postfix(ReleaseContraptionController __instance)
        { try { Begin(__instance); } catch (Exception error) { Clear(); BattleStatisticsPlugin.Logger.LogWarning("Sentry missile preview unavailable: " + error.Message); } }
    }
    [HarmonyPatch(typeof(CommanderSkillReleaseView), nameof(CommanderSkillReleaseView.UpdatePosition))]
    private static class UpdatePosition
    {
        private static void Postfix(CommanderSkillReleaseView __instance)
        { try { Refresh(__instance); } catch (Exception error) { Clear(); BattleStatisticsPlugin.Logger.LogWarning("Sentry missile preview failed: " + error.Message); } }
    }
    [HarmonyPatch(typeof(ReleaseContraptionController), nameof(ReleaseContraptionController.Exit))]
    private static class Exit { private static void Prefix() => Clear(); }
    [HarmonyPatch(typeof(ReleaseContraptionController), nameof(ReleaseContraptionController.Release))]
    private static class Release { private static void Prefix() => Clear(); }
    [HarmonyPatch(typeof(CommanderSkillReleaseView), nameof(CommanderSkillReleaseView.Hide))]
    private static class Hide { private static void Prefix() => Clear(); }
}
