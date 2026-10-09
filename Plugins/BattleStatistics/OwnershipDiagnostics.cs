using GameRiver;
using GameRiver.Fight;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Trace entry creation and identity observation separately: a getter may mutate
// native statistics, while the managed XP supplement can create a row itself.
internal static class OwnershipDiagnostics
{
    private static readonly HashSet<string> Seen = new();
    internal static void Clear() => Seen.Clear();
    internal static void Report(string path, long round, int team, string identity, int owner)
    {
        if (owner == team || Seen.Count >= 32 || !Seen.Add($"{path}:{round}:{team}:{identity}")) return;
        BattleStatisticsPlugin.Logger.LogWarning($"Statistics ownership trace: path={path}, team={team}, owner={owner}, identity={identity}; "
            + new System.Diagnostics.StackTrace(1, false));
    }

    [HarmonyPatch(typeof(RoundStatisticData), nameof(RoundStatisticData.CreateStatisticData))]
    private static class Created
    {
        private static void Prefix(RoundStatisticData __instance, int __0, IDamageRecorder __1)
        {
            try
            {
                if (__1?.GetCurrentTeamController() is { } owner)
                    Report("native-create", __instance.Pointer.ToInt64(), __0,
                        $"{__1.GetDamageRecorderType()}:{__1.GetID()}", owner.GetTeamIndex());
            }
            catch (Exception) { /* Diagnostics must never interrupt native statistics. */ }
        }
    }
}
