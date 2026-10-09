using GameRiver;
using GameRiver.Client;
using GameRiver.Fight;
using HarmonyLib;
using System.Reflection;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Observe only the native survivor scoring pass, never UI score queries or forecasts.
internal static class CoreDamageHooks
{
    [ThreadStatic] private static bool _aliveScore;
    [ThreadStatic] private static int _hpDepth;
    private static long _warningAfter;
    [HarmonyPatch(typeof(FightResultController), nameof(FightResultController.CalculateScore))]
    private static class ScorePass
    {
        private static void Prefix(FightResultController __instance, FightTeam __0, bool __1, out bool __state)
        {
            __state = _aliveScore;
            _aliveScore = __1 && __instance.match?.Pointer == MatchClient.Current?.Pointer;
            if (!_aliveScore) return;
            // Native callers can inline GetScore; also read at the scoring boundary.
            try
            {
                var meches = __0.GetMeches();
                var count = meches.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<FightMech>>().Count;
                for (var i = 0; i < count; i++)
                    if (meches[i] is { } mech && mech.IsAlive())
                        RecordingHooks.Observer?.CoreScoreObserved(mech, mech.GetScore());
            }
            catch (Exception e) { Warn(e); }
        }
        private static void Finalizer(bool __state) => _aliveScore = __state;
    }
    [HarmonyPatch(typeof(FightMech), nameof(FightMech.GetScore))]
    private static class UnitScore
    {
        private static void Postfix(FightMech __instance, int __result)
        {
            if (!_aliveScore) return;
            try { RecordingHooks.Observer?.CoreScoreObserved(__instance, __result); }
            catch (Exception e) { Warn(e); }
        }
    }
    [HarmonyPatch]
    private static class HpChange
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] {
            AccessTools.Method(typeof(TeamScoreGaugeReduce), nameof(TeamScoreGaugeReduce.ChangeScore)),
            AccessTools.Method(typeof(TeamScoreGaugeReduce), nameof(TeamScoreGaugeReduce.RefreshScore)) };
        private static void Prefix(TeamScoreGaugeReduce __instance, out (int Depth, long? Before) __state)
        {
            var depth = _hpDepth++; __state = (depth, null);
            try
            {
                if (depth == 0 && __instance.match?.Pointer == MatchClient.Current?.Pointer)
                    __state = (depth, __instance.GetScore());
            }
            catch (Exception e) { Warn(e); }
        }
        private static void Postfix(TeamScoreGaugeReduce __instance, (int Depth, long? Before) __state)
        {
            if (__state.Before is not { } before) return;
            try { RecordingHooks.Observer?.PlayerHpObserved(__instance.teamController.GetTeamIndex(), before, __instance.GetScore()); }
            catch (Exception e) { Warn(e); }
        }
        private static void Finalizer((int Depth, long? Before) __state) => _hpDepth = __state.Depth;
    }
    private static void Warn(Exception e)
    {
        if (Environment.TickCount64 < _warningAfter) return;
        _warningAfter = Environment.TickCount64 + 15000;
        BattleStatisticsPlugin.Logger.LogWarning("Core damage observation: " + e.Message);
    }
}
