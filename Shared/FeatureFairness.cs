namespace MechaCommunityMod.Configuration;

internal enum FairnessLevel { Green, Yellow, Red }

internal sealed record FairnessDefinition(FairnessLevel Level, string Title, string Description, string Color)
{
    internal string Badge => Level.ToString().ToUpperInvariant();
}

internal sealed record FeatureRating(string Id, FairnessLevel Level, string Reason);

// Project assessments of competitive impact, shared by the launcher and Setup.
internal static class FeatureFairness
{
    internal const string Disclaimer = "Mods in Mechabellum are a polarizing topic. Some mods can give an unfair advantage. "
        + "Do not use features that can give you an unfair advantage in ranked games, tournaments, or other competitive environments. "
        + "Follow the rules set by the game's developers and event organizers. "
        + "These categories are our own descriptions, not official approval.";

    internal static readonly FairnessDefinition[] Levels = {
        new(FairnessLevel.Green, "Post-game analysis",
            "Information shown after a match and cosmetic presentation changes. These do not assist decisions during an active match.", "#186A3B"),
        new(FairnessLevel.Yellow, "Live information",
            "Live information or analysis that may help decisions during a match. Use in competitive play only when its rules explicitly allow it.", "#806000"),
        new(FairnessLevel.Red, "Targeting assistance",
            "Direct tactical assistance, such as precise targeting previews, that can give an unfair advantage. Keep these features disabled in competitive play.", "#B42318")
    };

    internal static readonly FeatureRating[] Features = {
        new("Features/LiveOverlay", FairnessLevel.Yellow, "Shows live damage and army statistics while you can still make match decisions."),
        new("Features/IndividualStats", FairnessLevel.Yellow, "Shows detailed unit statistics during a match and may help tactical decisions."),
        new("Features/PostGameStats", FairnessLevel.Green, "Reviews completed matches without assisting decisions in that active match."),
        new("Features/BeaconSelectionPreview", FairnessLevel.Red, "Highlights units affected by a beacon before you commit its placement. Disabled by default."),
        new("Features/SentryMissilePreview", FairnessLevel.Red, "Highlights eligible enemy units and buildings in a sentry missile's trigger range before placement. Disabled by default.")
    };

    internal static FeatureRating For(string id) => Features.Single(f => f.Id == id);
    internal static FairnessDefinition Definition(FairnessLevel level) => Levels.Single(l => l.Level == level);
    internal static string Caption(SettingDefinition setting) => setting.Label;

    internal static void Export(string path)
    {
        var settings = SettingsCatalog.All.Where(s => s.Section == "Features").ToArray();
        if (!settings.Select(s => s.Id).OrderBy(id => id).SequenceEqual(Features.Select(f => f.Id).OrderBy(id => id)))
            throw new InvalidDataException("Every feature needs exactly one fairness rating.");
        var data = new {
            disclaimer = Disclaimer,
            levels = Levels.Select(l => new { badge = l.Badge, title = l.Title, description = l.Description, color = l.Color }),
            features = settings.Select(s => new { id = s.Id, key = s.Key, label = s.Label, caption = Caption(s),
                level = Definition(For(s.Id).Level).Badge, reason = For(s.Id).Reason, defaultValue = s.Default })
        };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
