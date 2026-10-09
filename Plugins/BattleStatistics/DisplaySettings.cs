using BepInEx.Configuration;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class DisplaySettings
{
    internal static TableDisplaySettings PostGame = null!, LiveUnit = null!;
    internal static ConfigEntry<bool> TableIndividuals = null!;
    internal static void Bind(ConfigFile config)
    {
        config.SaveOnConfigSet = true;
        PostGame = new(config, "Statistics");
        LiveUnit = new(config, "LiveUnitStatistics");
        TableIndividuals = config.Bind("Statistics", "TableIndividuals",
            DefaultSettings.Bool("Statistics", "TableIndividuals", false),
            "Show each named unit or squad as its own post-game table row.");
    }
}

internal sealed class TableDisplaySettings
{
    internal readonly ConfigEntry<bool> Dealt, Tanked, Kills, Costs, Hacking, Sources, Contributions,
        TableDamageOverkill, TableTankedOverkill, TableXpGained, TableXpFed, CoreDamage,
        TableHackAmount, TableFailedHack, Levels;

    internal TableDisplaySettings(ConfigFile config, string section)
    {
        ConfigEntry<bool> Entry(string name, string description, bool value = true) =>
            config.Bind(section, name, DefaultSettings.Bool(section, name, value), description);
        var overkill = true; var xp = true;
        if (section == "Statistics")
        {
            overkill = Entry("SplitOverkill", "Legacy default for separate overkill columns.").Value;
            xp = Entry("Experience", "Legacy default for XP columns.").Value;
        }
        Dealt = Entry("DamageDealt", "Show damage dealt.");
        CoreDamage = Entry("CoreDamage", "Show verified contributions to opposing player HP damage.");
        Tanked = Entry("DamageTanked", "Show damage tanked.");
        Kills = Entry("Kills", "Show kill counts and spell uses.");
        Costs = Entry("UnitCosts", "Show deployment value in reports.");
        Levels = Entry("UnitLevels", "Show recorded levels and progress toward the next level.");
        Hacking = Entry("Hacking", "Show hacking details.");
        TableHackAmount = Entry("TableHackAmount", "Show hack progress from successful conversion attempts.", Hacking.Value);
        TableFailedHack = Entry("TableFailedHack", "Show hack progress from attempts that ended without a conversion.", Hacking.Value);
        Sources = Entry("DamageSources", "Show damage source breakdowns and source colors.");
        Contributions = Entry("Contributions", "Show spawned and hacked unit contributions.");
        TableDamageOverkill = Entry("TableDamageOverkill", "Show the separate damage overkill column. Damage always shows effective damage.", overkill);
        TableTankedOverkill = Entry("TableTankedOverkill", "Show the separate tanked overkill column. Tanked always shows effective damage.", overkill);
        TableXpGained = Entry("TableXpGained", "Show XP gained in the statistics table.", xp);
        TableXpFed = Entry("TableXpFed", "Show actual enemy XP awarded by deaths, labelled XP fed.", xp);
    }

    internal StatsColumn[] Columns()
    {
        var columns = new List<StatsColumn>();
        if (Kills.Value) columns.Add(StatsColumn.Kills);
        if (Dealt.Value) { columns.Add(StatsColumn.Damage); if (TableDamageOverkill.Value) columns.Add(StatsColumn.DamageOverkill); }
        if (CoreDamage.Value) columns.Add(StatsColumn.CoreDamage);
        if (Tanked.Value) { columns.Add(StatsColumn.Tanked); if (TableTankedOverkill.Value) columns.Add(StatsColumn.TankedOverkill); }
        if (Hacking.Value) columns.Add(StatsColumn.Hacked);
        if (TableHackAmount.Value) columns.Add(StatsColumn.HackAmount);
        if (TableFailedHack.Value) columns.Add(StatsColumn.FailedHack);
        if (TableXpGained.Value) columns.Add(StatsColumn.XpGained);
        if (TableXpFed.Value) columns.Add(StatsColumn.XpFed);
        if (Costs.Value) columns.Add(StatsColumn.Supply);
        return columns.ToArray();
    }
}
