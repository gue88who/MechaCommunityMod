using GameRiver;
using GameRiver.Client;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Observers only. Never skip or change game methods or their arguments/results.
internal static class RecordingHooks
{
    internal static BattleStatisticsBehaviour? Observer;

    [HarmonyPatch(typeof(MatchClient), nameof(MatchClient.OnFightStart))]
    private static class FightStart
    {
        private static void Postfix(MatchClient __instance)
        {
            if (__instance.Pointer == MatchClient.Current?.Pointer) Observer?.FightStarted();
        }
    }

    [HarmonyPatch(typeof(BattleStatisticManager), nameof(BattleStatisticManager.OnFightEnd))]
    private static class RoundEnd
    {
        private static void Prefix(BattleStatisticManager __instance) => Observer?.RoundEnding(__instance);
        private static void Postfix(BattleStatisticManager __instance) => Observer?.RoundEnded(__instance);
    }

    [HarmonyPatch(typeof(Match), nameof(Match.OnFightEnd))]
    private static class Settlement
    {
        private static void Postfix(Match __instance)
        {
            if (__instance.Pointer == MatchClient.Current?.Pointer) Observer?.RoundSettled();
        }
    }

    [HarmonyPatch(typeof(MatchClient), nameof(MatchClient.ShowResultWindow))]
    private static class MatchEnd
    {
        private static void Postfix(MatchClient __instance, GameOverInfo __0)
        {
            if (__instance.Pointer == MatchClient.Current?.Pointer)
                Observer?.MatchEnded(__0);
        }
    }
}
