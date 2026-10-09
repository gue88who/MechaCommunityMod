namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class OverlayWindowPolicy
{
    internal static bool Blocks(bool specialModal, bool reinforcement, bool modalLayer,
        bool hasBackground, bool active, bool canvasEnabled, bool contentActive, bool tooltip = false) =>
        active && canvasEnabled && (reinforcement || tooltip ? contentActive : specialModal || (modalLayer && hasBackground));
}
