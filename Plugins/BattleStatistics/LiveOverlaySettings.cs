using BepInEx.Configuration;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Live and table settings use independent defaults and saved entries.
internal static class LiveOverlaySettings
{
    internal static ConfigEntry<bool> HideByDefault = null!, Damage = null!, Tanked = null!, Kills = null!,
        DamageOverkill = null!, TankedOverkill = null!, Hacked = null!, HackAmount = null!, FailedHack = null!,
        SourceColors = null!, XpGained = null!, XpFed = null!;

    internal static void Bind(ConfigFile config)
    {
        config.SaveOnConfigSet = true;
        ConfigEntry<bool> Entry(string name, string description, bool initial) => config.Bind("LiveOverlay", name, DefaultSettings.Bool("LiveOverlay", name, initial), description);
        HideByDefault = Entry("HideByDefault", "Start with the F8 panels hidden on the next game launch. F8 still toggles them during play.", !BattleStatisticsPlugin.StartVisible.Value);
        Damage = Entry("Damage", "Show the damage dealt bar in the F8 panels.", true);
        Tanked = Entry("Tanked", "Show the damage received bar in the F8 panels.", true);
        Kills = Entry("Kills", "Show kills and spell uses in the F8 panels.", true);
        DamageOverkill = Entry("DamageOverkill", "Split outgoing effective damage and overkill into separate amounts and colors.", false);
        TankedOverkill = Entry("TankedOverkill", "Split incoming effective damage and overkill into separate amounts and colors.", false);
        Hacked = Entry("Hacked", "Show the count of converted combat units.", false);
        HackAmount = Entry("HackAmount", "Show progress from successful conversion attempts.", false);
        FailedHack = Entry("FailedHack", "Show progress from attempts that ended without a conversion.", false);
        SourceColors = Entry("SourceColors", "Color damage segments by regular attack, technology, spell or other effect.", false);
        XpGained = Entry("XpGained", "Show experience actually received by units.", false);
        XpFed = Entry("XpFed", "Show experience actually awarded to enemies.", false);
    }
}
