using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class DamagePalette
{
    internal static readonly Color DamageOverkill = new(0.68f, 0.32f, 0.95f, 1);
    internal static readonly Color TankedOverkill = new(1f, 0.79f, 0.12f, 1);
    internal static readonly Color HackAmount = new(0.35f, 0.87f, 0.61f, 0.55f);
    internal static readonly Color FailedHack = new(1, 0.58f, 0.36f, 0.55f);
    internal static readonly Color Kills = new(1, 0.71f, 0.25f, 1);
    internal static readonly Color Experience = new(0.5f, 0.9f, 0.65f, 1);
    internal static Color ColorFor(DamageCategory category, bool dealt) => category switch
    {
        DamageCategory.Attack => dealt ? new Color(0.79f, 0.16f, 0.09f) : new Color(0.12f, 0.42f, 0.68f),
        DamageCategory.Tech => new Color(1f, 0.48f, 0.12f),
        DamageCategory.Spell => new Color(0.18f, 0.85f, 0.75f),
        _ => new Color(0.45f, 0.49f, 0.56f)
    };
    internal static string Name(DamageCategory category) => category switch
    {
        DamageCategory.Attack => "REGULAR ATTACKS",
        DamageCategory.Tech => "TECHNOLOGY",
        DamageCategory.Spell => "SPELLS",
        _ => "OTHER EFFECTS"
    };
}
