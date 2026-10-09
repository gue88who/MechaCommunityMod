using System.Diagnostics;
using System.Text.Json;

namespace MechaCommunityMod.Configuration;

internal sealed class Installation
{
    private readonly Action _requireClosed;
    internal string GameDirectory { get; }
    internal string ModDirectory => Path.Combine(GameDirectory, "MechaCommunityMod");
    internal string ConfigPath => Path.Combine(GameDirectory, "BepInEx", "config", "mecha.community.mod.battle-statistics.cfg");
    private string LoaderConfig => Path.Combine(GameDirectory, "BepInEx", "config", "BepInEx.cfg");
    internal string DefaultsPath => Path.Combine(ModDirectory, "default-settings.json");
    internal string Uninstaller => Path.Combine(ModDirectory, "setup", "unins000.exe");
    internal Installation(string directory, Action? requireClosed = null)
    { GameDirectory = Path.GetFullPath(directory); _requireClosed = requireClosed ?? RequireGameClosed; }
    internal void Validate(bool requireGameFiles = true)
    {
        if (requireGameFiles && (!File.Exists(Path.Combine(GameDirectory, "Mechabellum.exe"))
            || !File.Exists(Path.Combine(GameDirectory, "GameAssembly.dll"))
            || !Directory.Exists(Path.Combine(GameDirectory, "Mechabellum_Data"))))
            throw new InvalidOperationException("This launcher must be installed in Mechabellum/MechaCommunityMod/Launcher.");
        EnsureNoLinks(GameDirectory, false);
    }
    internal static void RequireGameClosed()
    {
        var processes = Process.GetProcessesByName("Mechabellum");
        try { if (processes.Length > 0) throw new InvalidOperationException("Close Mechabellum before changing settings or uninstalling."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    internal Dictionary<string, string> ReadSettings()
    {
        var values = SettingsCatalog.ReadDefaults(DefaultsPath); var config = IniDocument.Read(ConfigPath);
        TableSettingsMigration.Seed(config, values);
        foreach (var definition in SettingsCatalog.All)
            if (config.Get(definition.Section, definition.Key) is { } value && definition.Valid(value)) values[definition.Id] = value;
        return values;
    }
    internal void Apply(IReadOnlyDictionary<string, string> values, bool onlyMissing = false)
    {
        Validate(); _requireClosed();
        foreach (var entry in values)
        {
            var definition = SettingsCatalog.All.SingleOrDefault(s => s.Id == entry.Key)
                ?? throw new InvalidDataException("Unknown setting: " + entry.Key);
            if (!definition.Valid(entry.Value)) throw new InvalidDataException("Invalid setting: " + entry.Key);
        }
        EnsureNoLinks(Path.Combine(GameDirectory, "BepInEx"), false);
        EnsureNoLinks(Path.GetDirectoryName(ConfigPath)!, false);
        EnsureNoLinks(ConfigPath, false); EnsureNoLinks(ConfigPath + ".launcher.tmp", false);
        EnsureNoLinks(ModDirectory, false); EnsureNoLinks(LoaderConfig, false); EnsureNoLinks(LoaderConfig + ".launcher.tmp", false);
        var config = IniDocument.Read(ConfigPath);
        // Seed from the old values before applying edits to either new profile.
        TableSettingsMigration.Seed(config, SettingsCatalog.ReadDefaults(DefaultsPath));
        foreach (var definition in SettingsCatalog.All)
            if (values.TryGetValue(definition.Id, out var value) && (!onlyMissing || config.Get(definition.Section, definition.Key) is null))
                config.Set(definition.Section, definition.Key, value);
        config.Save(ConfigPath);
        HideConsole();
    }
    private void HideConsole()
    {
        var config = IniDocument.Read(LoaderConfig);
        var original = Path.Combine(ModDirectory, "console-original.txt");
        EnsureNoLinks(original, false);
        if (!File.Exists(original)) File.WriteAllText(original, config.Get("Logging.Console", "Enabled") ?? "<missing>");
        config.Set("Logging.Console", "Enabled", "false");
        config.Save(LoaderConfig);
    }
    internal void Initialize(bool ownsLoader)
    {
        Validate(); _requireClosed(); MigrateLegacyInstallation();
        Apply(SettingsCatalog.ReadDefaults(DefaultsPath), true);
        var loader = IniDocument.Read(LoaderConfig);
        var original = Path.Combine(ModDirectory, "unity-source-original.txt");
        EnsureNoLinks(original, false);
        if (!File.Exists(original)) File.WriteAllText(original, loader.Get("IL2CPP", "UnityBaseLibrariesSource") ?? "<missing>");
        loader.Set("IL2CPP", "UnityBaseLibrariesSource", "2022.3.62.zip"); loader.Save(LoaderConfig);
        if (ownsLoader) File.WriteAllText(Path.Combine(ModDirectory, "owns-loader.txt"), "Installed by Battle Statistics");
    }
    private void MigrateLegacyInstallation()
    {
        // The original statistics-only release used these names. They remain
        // here solely to migrate it; all new files use the product/plugin names.
        var oldConfig = WithinGame("BepInEx/config/tools.replay.damage-overlay.cfg");
        EnsureNoLinks(oldConfig, false); EnsureNoLinks(ConfigPath, false); EnsureNoLinks(ConfigPath + ".launcher.tmp", false);
        if (File.Exists(oldConfig))
        {
            var current = IniDocument.Read(ConfigPath);
            current.MergeMissing(IniDocument.Read(oldConfig)); current.Save(ConfigPath);
            File.Delete(oldConfig);
        }
        var oldDirectory = WithinGame("DamageOverlay");
        EnsureNoLinks(oldDirectory, true);
        foreach (var name in new[] { "owns-loader.txt", "console-original.txt", "unity-source-original.txt" })
        {
            var old = Path.Combine(oldDirectory, name); var replacement = Path.Combine(ModDirectory, name);
            EnsureNoLinks(replacement, false);
            if (File.Exists(old) && !File.Exists(replacement)) File.Copy(old, replacement);
        }
        // Prevent two copies of the plugin from observing the same combat.
        foreach (var name in new[] { "DamageOverlay.dll", "LICENSE.txt", "INSTALL.txt" })
            DeleteFile("BepInEx/plugins/DamageOverlay/" + name);
        DeleteFile("Damage Overlay Launcher.lnk");
        var manifest = Path.Combine(oldDirectory, "manifest.json");
        if (File.Exists(manifest))
        {
            using var previous = JsonDocument.Parse(File.ReadAllText(manifest));
            if (previous.RootElement.TryGetProperty("overlayVersion", out _)) DeleteDirectory("DamageOverlay");
        }
        var oldPlugin = WithinGame("BepInEx/plugins/DamageOverlay");
        EnsureNoLinks(oldPlugin, false);
        if (Directory.Exists(oldPlugin) && !Directory.EnumerateFileSystemEntries(oldPlugin).Any()) Directory.Delete(oldPlugin);
    }
    internal bool CanRemoveLoader()
    {
        if (!File.Exists(Path.Combine(ModDirectory, "owns-loader.txt"))) return false;
        var bep = Path.Combine(GameDirectory, "BepInEx");
        EnsureNoLinks(bep, true); EnsureNoLinks(Path.Combine(GameDirectory, "dotnet"), true);
        var plugins = Path.Combine(bep, "plugins");
        var ownPlugin = Path.Combine(plugins, "MechaCommunityMod", "BattleStatistics") + Path.DirectorySeparatorChar;
        if (Directory.Exists(plugins) && Directory.EnumerateFiles(plugins, "*", SearchOption.AllDirectories)
            .Any(p => !Path.GetFullPath(p).StartsWith(ownPlugin, StringComparison.OrdinalIgnoreCase))) return false;
        var patchers = Path.Combine(bep, "patchers");
        if (Directory.Exists(patchers) && Directory.EnumerateFiles(patchers, "*", SearchOption.AllDirectories).Any()) return false;
        var config = Path.Combine(bep, "config");
        if (Directory.Exists(config) && Directory.EnumerateFiles(config, "*", SearchOption.AllDirectories)
            .Any(p => p != LoaderConfig && p != ConfigPath)) return false;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModDirectory, "manifest.json")));
        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.RootElement.GetProperty("loaderFiles").EnumerateArray())
        {
            var path = WithinGame(file.GetProperty("path").GetString()!);
            EnsureNoLinks(path, false);
            knownFiles.Add(path);
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            if (!string.Equals(actual, file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) return false;
        }
        foreach (var relative in new[] { "BepInEx/core", "dotnet" })
        {
            var directory = WithinGame(relative);
            if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any(p => !knownFiles.Contains(p))) return false;
        }
        if (Directory.Exists(bep) && Directory.EnumerateFileSystemEntries(bep).Any(p => !new[] {
            "core", "cache", "config", "interop", "unity-libs", "plugins", "patchers", "LogOutput.log", "ErrorLog.log" }
            .Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))) return false;
        return true;
    }
    internal void Cleanup(bool removeStats)
    {
        Validate(false); _requireClosed();
        if (CanRemoveLoader())
        {
            DeleteDirectory("BepInEx"); DeleteDirectory("dotnet");
            foreach (var name in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }) DeleteFile(name);
        }
        else
        {
            var loader = IniDocument.Read(LoaderConfig);
            var original = Path.Combine(ModDirectory, "console-original.txt");
            var changed = false;
            if (File.Exists(original) && string.Equals(loader.Get("Logging.Console", "Enabled"), "false", StringComparison.OrdinalIgnoreCase))
            {
                var value = File.ReadAllText(original);
                loader.Set("Logging.Console", "Enabled", value == "<missing>" ? null : value); changed = true;
            }
            original = Path.Combine(ModDirectory, "unity-source-original.txt");
            if (File.Exists(original) && loader.Get("IL2CPP", "UnityBaseLibrariesSource") == "2022.3.62.zip")
            {
                var value = File.ReadAllText(original);
                loader.Set("IL2CPP", "UnityBaseLibrariesSource", value == "<missing>" ? null : value); changed = true;
            }
            if (changed) { EnsureNoLinks(LoaderConfig, false); EnsureNoLinks(LoaderConfig + ".launcher.tmp", false); loader.Save(LoaderConfig); }
            DeleteFile("BepInEx/config/mecha.community.mod.battle-statistics.cfg");
            DeleteFile("BepInEx/config/mecha.community.mod.battle-statistics.cfg.launcher.tmp");
        }
        if (removeStats)
        {
            var directory = WithinGame("ProjectDatas/Stats"); EnsureNoLinks(directory, false);
            if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory, "*.mechstats"))
                DeleteFile(Path.GetRelativePath(GameDirectory, path));
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }
    private string WithinGame(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(GameDirectory, relative));
        if (!path.StartsWith(GameDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path is outside the game folder.");
        return path;
    }
    private void DeleteFile(string relative)
    {
        var path = WithinGame(relative); EnsureNoLinks(path, false);
        if (File.Exists(path)) File.Delete(path);
    }
    private void DeleteDirectory(string relative)
    {
        var path = WithinGame(relative); EnsureNoLinks(path, true);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
    private static void EnsureNoLinks(string path, bool recursive)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A linked folder was found; leave this installation unchanged.");
        if (recursive && Directory.Exists(path))
            foreach (var item in Directory.EnumerateFileSystemEntries(path))
            {
                EnsureNoLinks(item, false);
                if (Directory.Exists(item)) EnsureNoLinks(item, true);
            }
    }
}
