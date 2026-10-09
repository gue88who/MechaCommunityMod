namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class TablePanning
{
    // The content follows the grabbed point; only numeric columns pan horizontally.
    internal static (float X, float Y) Move(float x, float y, float dragX, float dragY, float maxX, float maxY)
        => (Math.Clamp(x - dragX, 0, Math.Max(0, maxX)), Math.Clamp(y - dragY, 0, Math.Max(0, maxY)));
}
