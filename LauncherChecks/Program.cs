using MechaCommunityMod.Configuration;
using MechaCommunityMod;
using System.Security.Cryptography;
using System.Text.Json;

var checks = new (string Name, Action Run)[] {
    ("Unsupported or unverifiable builds launch vanilla with the loader disabled", () => WithGame(game => {
        var plan = GameLaunch.For(game.GameDirectory);
        Check(!plan.ModEnabled && plan.Message.Contains("Wait for a newer Mecha Community Mod build"), "Unsupported build was accepted");
        Check(plan.SteamArguments.SequenceEqual(new[] { "-applaunch", "669330", "--doorstop-enabled", "false" }), "Vanilla launch does not disable the loader");
        var supported = GameLaunch.From(new(true, "Verified exact game hashes"));
        Check(supported.ModEnabled && supported.SteamArguments.SequenceEqual(new[] { "-applaunch", "669330" }), "Supported build was disabled");
        Check(!File.Exists(Path.Combine(game.GameDirectory, "launch-state.json")), "Compatibility inspection changed the installation");
    })),
    ("Statistics rename preserves legacy preferences and loader ownership without loading duplicate plugins", () => WithGame(game => {
        var legacyConfig = Path.Combine(game.GameDirectory, "BepInEx/config/tools.replay.damage-overlay.cfg");
        File.WriteAllText(legacyConfig, "[Statistics]\nDamageDealt = false\n[LiveOverlay]\nXpFed = true\n[Features]\nIndividualStats = false\n[Other]\nKeep = legacy");
        File.WriteAllText(game.ConfigPath, "[Features]\nIndividualStats = true");
        var legacy = Path.Combine(game.GameDirectory, "DamageOverlay"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "owns-loader.txt"), "old owner");
        File.WriteAllText(Path.Combine(legacy, "console-original.txt"), "true");
        File.WriteAllText(Path.Combine(legacy, "manifest.json"), "{\"overlayVersion\":\"0.9.92\"}");
        var plugin = Path.Combine(game.GameDirectory, "BepInEx/plugins/DamageOverlay/DamageOverlay.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!); File.WriteAllText(plugin, "old plugin");
        game.Initialize(false);
        var settings = IniDocument.Read(game.ConfigPath);
        Check(settings.Get("Statistics", "DamageDealt") == "false" && settings.Get("LiveOverlay", "XpFed") == "true", "Legacy settings lost");
        Check(settings.Get("Features", "IndividualStats") == "true" && settings.Get("Other", "Keep") == "legacy", "New choice or unknown setting lost");
        Check(File.Exists(Path.Combine(game.ModDirectory, "owns-loader.txt")), "Ownership not migrated");
        Check(!File.Exists(plugin) && !File.Exists(legacyConfig) && !Directory.Exists(legacy), "Legacy installation remains");
        Check(game.CanRemoveLoader(), "Migrated ownership cannot be uninstalled");
    })),
    ("Config edits preserve unrelated settings/comments and remove duplicate assignments", () => {
        var ini = new IniDocument("# header\n[Other]\nLiveOverlay = untouched\n[Features]\nLiveOverlay = true\n# note\nLiveOverlay = false\n[Other2]\nValue = keep");
        ini.Set("Features", "LiveOverlay", "true");
        Check(ini.Get("Features", "LiveOverlay") == "true" && ini.Get("Other", "LiveOverlay") == "untouched"
            && ini.Get("Other2", "Value") == "keep" && ini.ToString().Contains("# note"), "Config edit lost data");
        Check(ini.ToString().Split("LiveOverlay = true").Length == 2, "Duplicate key survived");
        ini.Set("Features", "LiveOverlay", null); Check(ini.Get("Features", "LiveOverlay") is null, "Config removal failed");
    }),
    ("Preset defines all independent feature/overlay/table defaults and bounded layout settings", () => {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../installer/default-settings.json"));
        var values = SettingsCatalog.ReadDefaults(path);
        Check(SettingsCatalog.All.Select(s => s.Id).Distinct().Count() == SettingsCatalog.All.Length, "Duplicate catalog keys");
        Check(values["Features/LiveOverlay"] == "True" && values["LiveOverlay/XpGained"] == "False"
            && values["Statistics/TableXpGained"] == "True", "Defaults do not distinguish views");
        Check(!SettingsCatalog.All.Single(s => s.Id == "Display/Scale").Valid("9"), "Scale escaped native limits");
        Check(!SettingsCatalog.All.Single(s => s.Id == "Display/MaxRows").Valid("1.5"), "Fractional row counts were accepted");
    }),
    ("Initialization preserves preferences, independently seeds new switches, and disables the terminal", () => WithGame(game => {
        File.WriteAllText(game.ConfigPath, "[Statistics]\nDamageDealt = false\nTableXpGained = false\n[Other]\nKeep = 123");
        game.Initialize(false);
        var cfg = IniDocument.Read(game.ConfigPath);
        Check(cfg.Get("Statistics", "DamageDealt") == "false" && cfg.Get("Other", "Keep") == "123", "Update reset preferences");
        Check(cfg.Get("Features", "LiveOverlay") == "true" && cfg.Get("Features", "IndividualStats") == "true", "Missing feature defaults");
        Check(IniDocument.Read(Path.Combine(game.GameDirectory, "BepInEx/config/BepInEx.cfg")).Get("Logging.Console", "Enabled") == "false", "Terminal remains enabled");
        var values = game.ReadSettings(); values["Features/IndividualStats"] = "false"; values["LiveOverlay/Damage"] = "false"; game.Apply(values);
        Check(game.ReadSettings()["Features/IndividualStats"] == "false" && game.ReadSettings()["Features/PostGameStats"] == "true", "Feature switches are coupled");
        game.Initialize(false); Check(game.ReadSettings()["LiveOverlay/Damage"] == "false", "Second installation reset settings");
    })),
    ("Table preferences migrate once and live unit edits stay independent of post-game edits", () => WithGame(game => {
        File.WriteAllText(game.ConfigPath, "# Keep this\n[Statistics]\nDamageDealt = false\nUnitLevels = false\nDamageSources = false\nContributions = false\nTableIndividuals = true\nSplitOverkill = false\nExperience = false\nHacking = false\n[LiveUnitStatistics]\nUnitLevels = true\n");
        var preview = game.ReadSettings();
        Check(preview["LiveUnitStatistics/DamageDealt"] == "false" && preview["LiveUnitStatistics/UnitLevels"] == "true",
            "Launcher preview ignored old preferences or replaced an existing live preference");
        Check(!File.ReadAllText(game.ConfigPath).Contains("[Features]"), "Reading preferences wrote configuration");
        game.Initialize(false);
        var config = IniDocument.Read(game.ConfigPath);
        foreach (var key in new[] { "DamageDealt", "DamageSources", "Contributions", "TableDamageOverkill", "TableTankedOverkill",
            "TableXpGained", "TableXpFed", "TableHackAmount", "TableFailedHack" })
            Check(config.Get("LiveUnitStatistics", key) == "false", "Migration missed " + key);
        Check(config.Get("LiveUnitStatistics", "UnitLevels") == "true", "Migration overwrote a live-unit preference");
        Check(config.Get("LiveUnitStatistics", "TableIndividuals") is null, "Grouping leaked into the live popup");
        game.Apply(new Dictionary<string, string> { ["Statistics/DamageDealt"] = "true", ["LiveUnitStatistics/UnitLevels"] = "false" });
        game.Initialize(false);
        var saved = game.ReadSettings();
        Check(saved["LiveUnitStatistics/DamageDealt"] == "false" && saved["Statistics/DamageDealt"] == "true",
            "Post-game edits or reinstall changed live columns");
        Check(saved["Statistics/UnitLevels"] == "false" && saved["LiveUnitStatistics/UnitLevels"] == "false",
            "Live changes did not persist");
        game.Apply(new Dictionary<string, string> { ["LiveUnitStatistics/UnitLevels"] = "true" });
        Check(game.ReadSettings()["Statistics/UnitLevels"] == "false", "Live edit changed post-game settings");
        Check(File.ReadAllText(game.ConfigPath).Contains("# Keep this"), "Migration lost unrelated comments");
    })),
    ("First save copies shared preferences before a post-game edit and preserves custom preset defaults", () => WithGame(game => {
        File.WriteAllText(game.ConfigPath, "[Statistics]\nDamageDealt = false");
        game.Apply(new Dictionary<string, string> { ["Statistics/DamageDealt"] = "true" });
        Check(game.ReadSettings()["LiveUnitStatistics/DamageDealt"] == "false", "First edit was incorrectly copied into live defaults");
        File.WriteAllText(game.DefaultsPath, "{\"schemaVersion\":1,\"settings\":{\"Statistics/UnitLevels\":false,\"LiveUnitStatistics/DamageDealt\":false}}");
        var preset = SettingsCatalog.ReadDefaults(game.DefaultsPath);
        Check(preset["LiveUnitStatistics/UnitLevels"] == "False" && preset["LiveUnitStatistics/DamageDealt"] == "False",
            "Custom shared preset or explicit live preset was ignored");
        var manual = new IniDocument("[Statistics]\nDamageSources = false\n[Other]\nKeep = yes");
        Check(TableSettingsMigration.Seed(manual, preset), "Manual plugin migration did not run");
        manual.Set("Statistics", "DamageSources", "true");
        Check(!TableSettingsMigration.Seed(manual, preset) && manual.Get("LiveUnitStatistics", "DamageSources") == "false",
            "Manual plugin migration ran again and coupled the profiles");
        Check(manual.Get("Other", "Keep") == "yes", "Migration changed unrelated settings");
    })),
    ("Invalid launcher values cannot change config or escape the setting catalog", () => WithGame(game => {
        var before = File.ReadAllText(game.ConfigPath);
        Reject(() => game.Apply(new Dictionary<string, string> { ["Display/Scale"] = "100" }));
        Reject(() => game.Apply(new Dictionary<string, string> { ["Other/Arbitrary"] = "true" }));
        Check(File.ReadAllText(game.ConfigPath) == before, "Invalid values partially saved");
    })),
    ("Fresh owned loader is completely removed while game files/replays/stats remain", () => WithGame(game => {
        game.Initialize(true); Check(game.CanRemoveLoader(), "Own loader not recognized");
        game.Cleanup(false);
        Check(!Directory.Exists(Path.Combine(game.GameDirectory, "BepInEx")) && !Directory.Exists(Path.Combine(game.GameDirectory, "dotnet"))
            && !File.Exists(Path.Combine(game.GameDirectory, "winhttp.dll")), "Owned loader remained");
        Check(File.Exists(Path.Combine(game.GameDirectory, "GameAssembly.dll")) && File.Exists(Path.Combine(game.GameDirectory, "ProjectDatas/Replay/test.grbr"))
            && File.Exists(Path.Combine(game.GameDirectory, "ProjectDatas/Stats/test.mechstats")), "Game/user data was deleted");
    })),
    ("Existing shared loader is retained and its prior console/library settings restored", () => WithGame(game => {
        game.Initialize(false); Check(!game.CanRemoveLoader(), "Pre-existing loader became owned"); game.Cleanup(false);
        var loader = IniDocument.Read(Path.Combine(game.GameDirectory, "BepInEx/config/BepInEx.cfg"));
        Check(loader.Get("Logging.Console", "Enabled") == "true" && loader.Get("IL2CPP", "UnityBaseLibrariesSource") == "https://example.test/unity.zip", "Shared config was not restored");
        Check(File.Exists(Path.Combine(game.GameDirectory, "BepInEx/core/loader.dll")) && !File.Exists(game.ConfigPath), "Shared loader or plugin config cleanup wrong");
    })),
    ("Installing another plugin after our loader prevents shared-loader deletion", () => WithGame(game => {
        game.Initialize(true); var other = Path.Combine(game.GameDirectory, "BepInEx/plugins/Other/other.dll"); Directory.CreateDirectory(Path.GetDirectoryName(other)!); File.WriteAllText(other, "other mod");
        Check(!game.CanRemoveLoader(), "Other mod was ignored"); game.Cleanup(false);
        Check(File.ReadAllText(other) == "other mod" && File.Exists(Path.Combine(game.GameDirectory, "winhttp.dll")), "Uninstall damaged another mod");
    })),
    ("Changed loader binaries and unexpected runtime files are preserved", () => {
        WithGame(game => { game.Initialize(true); File.WriteAllText(Path.Combine(game.GameDirectory, "BepInEx/core/loader.dll"), "changed"); Check(!game.CanRemoveLoader(), "Modified loader was removable"); });
        WithGame(game => { game.Initialize(true); File.WriteAllText(Path.Combine(game.GameDirectory, "dotnet/foreign.txt"), "foreign"); Check(!game.CanRemoveLoader(), "Foreign runtime file was removable"); });
    }),
    ("Optional stats removal deletes only reports and preserves replays/other files", () => WithGame(game => {
        game.Initialize(false); game.Cleanup(true);
        Check(!File.Exists(Path.Combine(game.GameDirectory, "ProjectDatas/Stats/test.mechstats"))
            && File.Exists(Path.Combine(game.GameDirectory, "ProjectDatas/Stats/keep.txt"))
            && File.Exists(Path.Combine(game.GameDirectory, "ProjectDatas/Replay/test.grbr")), "Stats cleanup exceeded its scope");
    })),
    ("Mod cleanup still works after Steam removes the game executable and data", () => WithGame(game => {
        game.Initialize(true);
        File.Delete(Path.Combine(game.GameDirectory, "Mechabellum.exe"));
        File.Delete(Path.Combine(game.GameDirectory, "GameAssembly.dll"));
        Directory.Delete(Path.Combine(game.GameDirectory, "Mechabellum_Data"));
        game.Cleanup(false);
        Check(!Directory.Exists(Path.Combine(game.GameDirectory, "BepInEx"))
            && File.Exists(Path.Combine(game.GameDirectory, "ProjectDatas/Replay/test.grbr")), "Cleanup requires removed game files");
    }))
};
var failed = 0;
foreach (var (name, run) in checks)
    try { run(); Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
Console.WriteLine($"{checks.Length - failed}/{checks.Length} launcher checks passed.");
return failed > 0 ? 1 : 0;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid input was accepted"); }
static void WithGame(Action<Installation> action)
{
    var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures"));
    var directory = Path.GetFullPath(Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(directory);
    try
    {
        var files = new Dictionary<string, string> { ["Mechabellum.exe"] = "fixture", ["GameAssembly.dll"] = "game",
            ["BepInEx/core/loader.dll"] = "loader", ["dotnet/coreclr.dll"] = "runtime", ["winhttp.dll"] = "proxy",
            ["doorstop_config.ini"] = "doorstop", [".doorstop_version"] = "version",
            ["BepInEx/config/BepInEx.cfg"] = "[Logging.Console]\nEnabled = true\n[IL2CPP]\nUnityBaseLibrariesSource = https://example.test/unity.zip",
            ["BepInEx/config/mecha.community.mod.battle-statistics.cfg"] = "", ["BepInEx/plugins/MechaCommunityMod/BattleStatistics/MechaCommunityMod.BattleStatistics.dll"] = "mod",
            ["ProjectDatas/Stats/test.mechstats"] = "report", ["ProjectDatas/Stats/keep.txt"] = "user file", ["ProjectDatas/Replay/test.grbr"] = "replay" };
        foreach (var (relative, text) in files) { var file = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
        Directory.CreateDirectory(Path.Combine(directory, "Mechabellum_Data"));
        Directory.CreateDirectory(Path.Combine(directory, "MechaCommunityMod"));
        var manifest = new { loaderFiles = files.Where(p => p.Key is "BepInEx/core/loader.dll" or "dotnet/coreclr.dll" or "winhttp.dll" or "doorstop_config.ini" or ".doorstop_version")
            .Select(p => new { path = p.Key, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, p.Key)))) }).ToArray() };
        File.WriteAllText(Path.Combine(directory, "MechaCommunityMod/manifest.json"), JsonSerializer.Serialize(manifest));
        action(new Installation(directory, () => { }));
    }
    finally
    {
        if (!directory.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture cleanup");
        Directory.Delete(directory, true);
    }
}
