using GameRiver.Fight;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class SpellHooks
{
    private static long _nextWarning;

    // Waypoint abilities use a different release controller and never enter
    // CSRS_Perform. Observe its actual fight activation rather than placement.
    [HarmonyPatch(typeof(CSRC_WayPoint), nameof(CSRC_WayPoint.OnFightStart))]
    private static class WayPointStarted
    {
        private static void Postfix(CSRC_WayPoint __instance) => Record(__instance);
    }

    private static void Record(CommanderSkillReleaseController release)
    {
        try
        {
            var source = DamageSourceReader.Effect(release.commanderSkill);
            if (source.Category != DamageCategory.Spell) return;
            RecordingHooks.Observer?.SpellCast(release.teamController, source, release.Pointer.ToInt64());
        }
        catch (Exception e)
        {
            if (Environment.TickCount64 < _nextWarning) return;
            _nextWarning = Environment.TickCount64 + 15000;
            BattleStatisticsPlugin.Logger.LogWarning("Spell cast observation: " + e.Message);
        }
    }

    // Enter is the actual cast start, before sub-effects fly, land or get
    // intercepted. Placement/preview/cancellation during deployment is not a use.
    [HarmonyPatch(typeof(CSRS_Perform), nameof(CSRS_Perform.Enter))]
    private static class CastStarted
    {
        private static void Postfix(CSRS_Perform __instance)
        {
            Record(__instance.releaseController);
        }
    }
}
