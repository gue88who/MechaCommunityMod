using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MechaCommunityMod;

internal sealed record TargetBuild(int SchemaVersion, string GameVersion, string SteamBuildId,
    string GameAssemblySha256, string MetadataSha256);
internal sealed record CompatibilityResult(bool Compatible, string Message);

// Pure managed startup code: do not reference Unity, generated game bindings or Harmony here.
internal static class GameCompatibility
{
    internal const string MetadataPath = "Mechabellum_Data/il2cpp_data/Metadata/global-metadata.dat";

    internal static CompatibilityResult CheckInstalled(string gameRoot)
    {
        try
        {
            using var resource = typeof(GameCompatibility).Assembly
                .GetManifestResourceStream("MechaCommunityMod.compatibility.json")
                ?? throw new InvalidDataException("Embedded compatibility manifest is missing.");
            var target = JsonSerializer.Deserialize<TargetBuild>(resource,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var binaries = Check(gameRoot, target);
            if (!binaries.Compatible) return binaries;
            var manifest = Path.GetFullPath(Path.Combine(gameRoot, "..", "..", "appmanifest_669330.acf"));
            var steam = CheckSteamBuild(File.ReadAllText(manifest), target!);
            return steam.Compatible ? binaries : steam;
        }
        catch (Exception error)
        {
            return new(false, $"Mecha Community Mod disabled: game compatibility could not be verified ({error.Message}).");
        }
    }

    internal static CompatibilityResult CheckSteamBuild(string manifest, TargetBuild target)
    {
        var build = Regex.Match(manifest, "\"buildid\"\\s+\"([0-9]+)\"", RegexOptions.IgnoreCase);
        return build.Success && build.Groups[1].Value == target.SteamBuildId
            ? new(true, "Steam build verified.")
            : new(false, $"Mecha Community Mod disabled: expected Steam build {target.SteamBuildId}, "
                + "but the installed Steam build is different or cannot be verified. Wait for a newer Mecha Community Mod build.");
    }

    internal static CompatibilityResult Check(string gameRoot, TargetBuild? target)
    {
        if (target is null || target.SchemaVersion != 1 || string.IsNullOrWhiteSpace(target.GameVersion)
            || string.IsNullOrWhiteSpace(target.SteamBuildId)
            || !IsHash(target.GameAssemblySha256) || !IsHash(target.MetadataSha256))
            return new(false, "Mecha Community Mod disabled: invalid compatibility manifest.");

        var expected = $"Mechabellum {target.GameVersion} (Steam build {target.SteamBuildId})";
        try
        {
            foreach (var (relative, hash) in new[] {
                ("GameAssembly.dll", target.GameAssemblySha256), (MetadataPath, target.MetadataSha256) })
            {
                using var stream = File.OpenRead(Path.Combine(gameRoot, relative));
                using var algorithm = SHA256.Create();
                var actual = Convert.ToHexString(algorithm.ComputeHash(stream));
                if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Mecha Community Mod disabled: unsupported game binaries. Expected {expected}; "
                        + $"{relative} SHA-256 is {actual}. Wait for a newer Mecha Community Mod build targeting this game build.");
            }
            return new(true, $"Game compatibility verified: {expected}.");
        }
        catch (Exception error)
        {
            return new(false, $"Mecha Community Mod disabled: expected {expected}, but game files could not be verified ({error.Message}).");
        }
    }

    internal static bool Activate(CompatibilityResult result, Action load, Action<string> log)
    {
        log(result.Message);
        if (!result.Compatible) return false;
        load();
        return true;
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
