namespace MechaCommunityMod.Configuration;

internal static class TableSettingsMigration
{
    internal static bool Seed(IniDocument config, IReadOnlyDictionary<string, string> defaults)
    {
        var changed = false;
        foreach (var setting in SettingsCatalog.All.Where(s => s.Section == "LiveUnitStatistics"))
        {
            if (config.Get(setting.Section, setting.Key) is not null) continue;
            var previous = config.Get("Statistics", setting.Key);
            var legacyKey = setting.Key switch {
                "TableDamageOverkill" or "TableTankedOverkill" => "SplitOverkill",
                "TableXpGained" or "TableXpFed" => "Experience",
                "TableHackAmount" or "TableFailedHack" => "Hacking",
                _ => null
            };
            if (previous is null && legacyKey is not null) previous = config.Get("Statistics", legacyKey);
            config.Set(setting.Section, setting.Key,
                previous is not null && setting.Valid(previous) ? previous : defaults[setting.Id]);
            changed = true;
        }
        return changed;
    }
}
