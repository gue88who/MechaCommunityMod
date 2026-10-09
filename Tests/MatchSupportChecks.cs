using MechaCommunityMod.Plugins.BattleStatistics;

internal static class MatchSupportChecks
{
    internal static void Transitions()
    {
        // Exercise the production guard across mode changes without loading Unity.
        GameRiver.Client.MatchClient.Current = new();
        foreach (var mode in new[] { GameRiver.MatchMode.VS_1_1, GameRiver.MatchMode.VS_2_2,
            GameRiver.MatchMode.VS_4_Scuffle, GameRiver.MatchMode.VS_2_2_Scuffle,
            (GameRiver.MatchMode)999, GameRiver.MatchMode.VS_1_1 })
        {
            GameRiver.Client.MatchClient.BattleSetting = new() { MatchMode = mode };
            if (MatchSupport.Allowed != (mode == GameRiver.MatchMode.VS_1_1))
                throw new Exception("Incorrect stats availability after switching to " + mode);
        }
        GameRiver.Client.MatchClient.Current = null;
        if (MatchSupport.Allowed) throw new Exception("Old 1v1 settings enabled stats in the lobby");
        GameRiver.Client.MatchClient.Current = new();
        GameRiver.Client.MatchClient.BattleSetting = null;
        if (MatchSupport.Allowed) throw new Exception("Missing mode settings enabled stats");
        GameRiver.Client.MatchClient.Current = null;
    }
}

// Minimal native boundary doubles. MatchSupport itself is linked unchanged.
namespace GameRiver
{
    internal enum MatchMode { VS_1_1, VS_2_2, VS_4_Scuffle, VS_2_2_Scuffle }
    internal sealed class BattleSetting { internal MatchMode MatchMode { get; init; } }
}
namespace GameRiver.Client
{
    internal sealed class MatchClient
    {
        internal static MatchClient? Current { get; set; }
        internal static BattleSetting? BattleSetting { get; set; }
    }
}
