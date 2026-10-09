using GameRiver;
using GameRiver.Client;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class MatchSupport
{
    // Use the declared mode, not surviving army count: FFA can end with two
    // armies, and 2v2 can expose two allied teams. Missing settings fail closed.
    internal static bool Allowed => MatchClient.Current is not null
        && MatchClient.BattleSetting?.MatchMode == MatchMode.VS_1_1;
}
