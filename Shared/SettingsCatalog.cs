using System.Globalization;
using System.Text.Json;

namespace MechaCommunityMod.Configuration;

internal sealed record SettingDefinition(string Section, string Key, string Label, string Group,
    string Default, decimal? Minimum = null, decimal? Maximum = null, int Decimals = 0, string[]? Choices = null)
{
    internal string Id => Section + "/" + Key;
    internal bool IsBoolean => Minimum is null;
    internal bool Valid(string value) => Id == "Display/ToggleKey" ? System.Text.RegularExpressions.Regex.IsMatch(value,
        "^([A-Z]|Alpha[0-9]|Keypad[0-9]|F([1-9]|1[0-5])|Tab|Space|Return|Backspace|Delete|Insert|Home|End|PageUp|PageDown|UpArrow|DownArrow|LeftArrow|RightArrow|LeftShift|RightShift|LeftControl|RightControl|LeftAlt|RightAlt|CapsLock|Numlock|ScrollLock|Pause|Print|BackQuote|Minus|Equals|LeftBracket|RightBracket|Backslash|Semicolon|Quote|Comma|Period|Slash|KeypadPeriod|KeypadDivide|KeypadMultiply|KeypadMinus|KeypadPlus|KeypadEnter|KeypadEquals)$") : Choices is not null ? Choices.Contains(value) : IsBoolean ? bool.TryParse(value, out _)
        : decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number >= Minimum && number <= Maximum
            && (Decimals != 0 || decimal.Truncate(number) == number);
}

internal static class SettingsCatalog
{
    private static readonly SettingDefinition[] Common = {
        new("Display", "ToggleKey", "Overlay hotkey", "Overlay", "F8", Choices: new[] { "Tab", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" }),
        new("Features", "LiveOverlay", "Live battle stats overlay", "Features", "true"),
        new("Features", "IndividualStats", "Live individual unit stats", "Features", "true"),
        new("Features", "PostGameStats", "Post-game stats", "Features", "true"),
        new("Features", "BeaconSelectionPreview", "Beacon selection preview", "VisualHelpers", "false"),
        new("Features", "SentryMissilePreview", "Sentry missile range preview", "VisualHelpers", "false"),
        new("LiveOverlay", "HideByDefault", "Legacy startup preference", "Legacy", "false"),
        new("LiveOverlay", "BattleCentered", "Battle: centered scoreboard (off = sidebars)", "Overlay", "false"),
        new("LiveOverlay", "BattleOnDemand", "Battle: hold hotkey (off = toggle)", "Overlay", "false"),
        new("LiveOverlay", "DeploymentCentered", "Deployment: centered scoreboard (off = sidebars)", "Overlay", "true"),
        new("LiveOverlay", "DeploymentOnDemand", "Deployment: hold hotkey (off = toggle)", "Overlay", "true"),
        new("LiveOverlay", "Damage", "Damage", "Overlay", "true"),
        new("LiveOverlay", "Tanked", "Tanked", "Overlay", "true"),
        new("LiveOverlay", "Kills", "Kills / spell uses", "Overlay", "true"),
        new("LiveOverlay", "DamageOverkill", "Damage overkill", "Overlay", "false"),
        new("LiveOverlay", "TankedOverkill", "Tanked overkill", "Overlay", "false"),
        new("LiveOverlay", "Hacked", "Hacked unit count", "Overlay", "false"),
        new("LiveOverlay", "HackAmount", "Successful hack amount", "Overlay", "false"),
        new("LiveOverlay", "FailedHack", "Failed hack amount", "Overlay", "false"),
        new("LiveOverlay", "SourceColors", "Damage source colors", "Overlay", "false"),
        new("LiveOverlay", "XpGained", "XP gained", "Overlay", "false"),
        new("LiveOverlay", "XpFed", "XP fed", "Overlay", "false"),
        new("Statistics", "DamageDealt", "Damage", "Stats", "true"),
        new("Statistics", "CoreDamage", "Core damage", "Stats", "true"),
        new("Statistics", "DamageTanked", "Tanked", "Stats", "true"),
        new("Statistics", "Kills", "Kills / spell uses", "Stats", "true"),
        new("Statistics", "TableDamageOverkill", "Damage overkill", "Stats", "true"),
        new("Statistics", "TableTankedOverkill", "Tanked overkill", "Stats", "true"),
        new("Statistics", "TableXpGained", "XP gained", "Stats", "true"),
        new("Statistics", "TableXpFed", "XP fed", "Stats", "true"),
        new("Statistics", "TableIndividuals", "Individual units / squads", "Stats", "false"),
        new("Statistics", "UnitCosts", "Supply", "Stats", "true"),
        new("Statistics", "UnitLevels", "Levels / XP progress", "Stats", "true"),
        new("Statistics", "Hacking", "Hacked unit count", "Stats", "true"),
        new("Statistics", "TableHackAmount", "Successful hack amount", "Stats", "true"),
        new("Statistics", "TableFailedHack", "Failed hack amount", "Stats", "true"),
        new("Statistics", "DamageSources", "Damage source breakdowns", "Stats", "true"),
        new("Statistics", "Contributions", "Spawned / attributed units", "Stats", "true"),
        new("Display", "Scale", "Overlay size", "Layout", "1", .6m, 1.5m, 2),
        new("Display", "TopOffset", "Distance from top", "Layout", "220", 100, 650),
        new("Display", "MaxRows", "Unit types per page", "Overlay", "8", 1, 16),
        new("Display", "SpectatorMaxRows", "Unit types per page (spectating)", "Overlay", "16", 1, 24),
        new("Display", "RefreshSeconds", "Refresh interval (seconds)", "Layout", ".25", .1m, 2, 2)
    };

    internal static readonly SettingDefinition[] All = Common.Concat(Common
        .Where(s => s.Section == "Statistics" && s.Key != "TableIndividuals")
        .Select(s => s with { Section = "LiveUnitStatistics", Group = "UnitStats" })).ToArray();

    internal static Dictionary<string, string> ReadDefaults(string filename)
    {
        var values = All.ToDictionary(s => s.Id, s => s.Default, StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filename)) return values;
        using var document = JsonDocument.Parse(File.ReadAllText(filename));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported default-settings.json version.");
        var explicitDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.RootElement.GetProperty("settings").EnumerateObject())
        {
            var definition = All.FirstOrDefault(s => string.Equals(s.Id, entry.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Unknown setting: " + entry.Name);
            var value = entry.Value.ToString();
            if (!definition.Valid(value)) throw new InvalidDataException("Invalid default: " + entry.Name);
            values[definition.Id] = value;
            explicitDefaults.Add(definition.Id);
        }
        // Older distribution presets supplied one shared table profile.
        foreach (var definition in All.Where(s => s.Section == "LiveUnitStatistics"))
            if (!explicitDefaults.Contains(definition.Id)) values[definition.Id] = values["Statistics/" + definition.Key];
        return values;
    }
}
