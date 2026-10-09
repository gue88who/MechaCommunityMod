using BepInEx;
using MechaCommunityMod.Configuration;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class DefaultSettings
{
    internal static void Migrate(BepInEx.Configuration.ConfigFile config)
    {
        var legacy = Path.Combine(Paths.ConfigPath, "tools.replay.damage-overlay.cfg");
        if (!File.Exists(config.ConfigFilePath) && File.Exists(legacy))
        { File.Copy(legacy, config.ConfigFilePath); config.Reload(); }
        var settings = IniDocument.Read(config.ConfigFilePath);
        var defaultsPath = Path.Combine(Paths.GameRootPath, "MechaCommunityMod", "default-settings.json");
        if (TableSettingsMigration.Seed(settings, SettingsCatalog.ReadDefaults(defaultsPath)))
        { settings.Save(config.ConfigFilePath); config.Reload(); }
    }
    private static Dictionary<string, string>? _values;
    internal static void Load()
    {
        var path = Path.Combine(Paths.GameRootPath, "MechaCommunityMod", "default-settings.json");
        if (!File.Exists(path)) return; // Keep migration defaults for manual DLL installs.
        try { _values = SettingsCatalog.ReadDefaults(path); }
        catch (Exception error) { BattleStatisticsPlugin.Logger.LogWarning("Default settings: " + error.Message); }
    }
    internal static bool Bool(string section, string key, bool fallback) =>
        _values?.TryGetValue(section + "/" + key, out var text) == true && bool.TryParse(text, out var value) ? value : fallback;
    internal static float Number(string key, float fallback) =>
        _values?.TryGetValue("Display/" + key, out var text) == true
        && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

internal static class BattleStatisticsFeatures
{
    internal static BepInEx.Configuration.ConfigEntry<bool> LiveOverlay = null!, IndividualStats = null!, PostGameStats = null!;
    internal static void Bind(BepInEx.Configuration.ConfigFile config)
    {
        LiveOverlay = config.Bind("Features", "LiveOverlay", DefaultSettings.Bool("Features", "LiveOverlay", true), "Enable the F8 live battle statistics panels.");
        IndividualStats = config.Bind("Features", "IndividualStats", DefaultSettings.Bool("Features", "IndividualStats", true), "Enable the selected-unit statistics button and panel.");
        PostGameStats = config.Bind("Features", "PostGameStats", DefaultSettings.Bool("Features", "PostGameStats", true), "Enable post-game statistics and the Stats menu button.");
    }
}
