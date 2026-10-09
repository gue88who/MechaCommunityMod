using GameRiver.Client;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class ResultsWindowHooks
{
    private static long _nextWarning;

    // Tooltips can reuse an already-open window without calling GRWindow.Show.
    [HarmonyPatch(typeof(TooltipWindow), nameof(TooltipWindow.ShowTooltip))]
    private static class TooltipShown
    {
        private static void Postfix(TooltipWindow __instance)
        {
            try { OverlayUiLayer.Track(__instance); }
            catch (Exception e)
            {
                if (Environment.TickCount64 < _nextWarning) return;
                _nextWarning = Environment.TickCount64 + 15000;
                BattleStatisticsPlugin.Logger.LogWarning("Tooltip foreground tracking: " + e.Message);
            }
        }
    }

    // Show activates the native window. Add its action in the same call, before
    // the next rendered frame, including when the menu is recreated on reopen.
    [HarmonyPatch(typeof(GRWindow), nameof(GRWindow.Show))]
    private static class WindowShown
    {
        private static void Postfix(GRWindow __instance)
        {
            try
            {
                OverlayUiLayer.Track(__instance);
                if (__instance.TryCast<InGameOptionWindow>() is null
                    && __instance.TryCast<BattleResultWindow>() is null) return;
                RecordingHooks.Observer?.StatsWindowShown(__instance);
            }
            catch (Exception e)
            {
                if (Environment.TickCount64 < _nextWarning) return;
                _nextWarning = Environment.TickCount64 + 15000;
                BattleStatisticsPlugin.Logger.LogWarning("Stats window open: " + e.Message);
            }
        }
    }
    [HarmonyPatch(typeof(GRWindow), nameof(GRWindow.Hide))]
    private static class WindowHidden
    {
        private static void Postfix(GRWindow __instance)
        {
            try { OverlayUiLayer.Hidden(__instance); }
            catch (Exception e) { BattleStatisticsPlugin.Logger.LogWarning("Overlay window hide: " + e.Message); }
        }
    }
}
