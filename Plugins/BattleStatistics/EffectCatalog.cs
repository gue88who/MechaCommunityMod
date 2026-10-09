using GameRiver;
using GameRiver.Client;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Read the installed game's complete catalogs, including derived data types.
// Never infer an ability from another catalog that happens to use the same ID.
internal static class EffectCatalog
{
    private sealed record Entry(string Name, string Icon, SpriteType Type);
    private static readonly Dictionary<(DamageCategory, int), Entry> Entries = new();
    private static readonly Dictionary<(DamageCategory, int), Sprite> Sprites = new();
    private static bool _loaded, _iconsChecked;

    internal static void Load()
    {
        if (_loaded || Config.Instance == null) return;
        var spells = Config.Instance.GetCommanderSkillDatas();
        var techs = Config.Instance.GetTechnologyDatas();
        if (spells == null || techs == null) return;
        var spellCount = spells.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<CommanderSkillData>>().Count;
        var techCount = techs.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<TechnologyData>>().Count;
        if (spellCount == 0 || techCount == 0) return;
        for (var i = 0; i < spellCount; i++)
        {
            var data = spells[i];
            if (data != null) Entries[(DamageCategory.Spell, data.GetID())] = new(data.GetName(), data.GetIconName(), SpriteType.CommanderSkill);
        }
        for (var i = 0; i < techCount; i++)
        {
            var data = techs[i];
            if (data != null) Entries[(DamageCategory.Tech, data.GetID())] = new(data.GetName(), data.GetIconName(), SpriteType.Technology);
        }
        _loaded = true;
        BattleStatisticsPlugin.Logger.LogInfo($"Loaded native effect catalogs: {spellCount} commander skills, {techCount} technologies.");
    }

    internal static DamageSource Source(DamageCategory category, int id, string fallback)
    {
        Load();
        return new(category, id, Entries.TryGetValue((category, id), out var entry) ? entry.Name : fallback);
    }

    internal static Sprite? Portrait(DamageCategory category, int id)
    {
        Load();
        var key = (category, id);
        if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        if (!Entries.TryGetValue(key, out var entry) || string.IsNullOrWhiteSpace(entry.Icon)) return null;
        var manager = GRUIManager.Instance?.GetSpriteManager();
        if (manager == null) return null;
        var sprite = manager.GetSprite(entry.Icon, entry.Type, 0);
        if (sprite != null) Sprites[key] = sprite;
        return sprite;
    }

    internal static void CheckIcons()
    {
        Load();
        if (!_loaded || _iconsChecked || GRUIManager.Instance?.GetSpriteManager() == null) return;
        var missing = new List<string>();
        foreach (var (key, entry) in Entries)
            if (Portrait(key.Item1, key.Item2) == null) missing.Add($"{key.Item1}/{key.Item2}/{entry.Name} ({entry.Icon})");
        // Retry after asset loading; never cache missing sprites permanently.
        if (missing.Count > 0)
            BattleStatisticsPlugin.Logger.LogWarning($"Native effect icon audit: {missing.Count} unresolved icons: " + string.Join(", ", missing.Take(16)));
        else BattleStatisticsPlugin.Logger.LogInfo($"Native effect icon audit: all {Entries.Count} catalog icons resolved.");
        _iconsChecked = true;
    }
}
