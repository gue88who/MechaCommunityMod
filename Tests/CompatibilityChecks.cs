using System.Security.Cryptography;
using MechaCommunityMod.Plugins.BattleStatistics;
using MechaCommunityMod;

internal static class CompatibilityChecks
{
    internal static readonly (string, Action)[] All = {
        ("Matching game binaries allow exactly one startup", Match),
        ("Matching binaries cannot bypass a different or unverifiable Steam build", SteamBuild),
        ("Changed native code blocks all overlay startup", NativeMismatch),
        ("Changed metadata blocks startup even with matching native code", MetadataMismatch),
        ("Missing or unreadable files block startup", Missing),
        ("Invalid compatibility targets cannot enable the mod", Invalid),
        ("Missing embedded-target game installation is rejected", Embedded)
    };

    private static void Fixture(Action<string, TargetBuild> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "overlay-compatibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory, GameCompatibility.MetadataPath))!);
        try
        {
            File.WriteAllText(Path.Combine(directory, "GameAssembly.dll"), "fixture native code");
            File.WriteAllText(Path.Combine(directory, GameCompatibility.MetadataPath), "fixture metadata");
            string Hash(string relative) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, relative))));
            check(directory, new TargetBuild(1, "fixture", "1", Hash("GameAssembly.dll"), Hash(GameCompatibility.MetadataPath)));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Verify(CompatibilityResult result, bool expected)
    {
        var called = 0;
        var messages = new List<string>();
        var started = GameCompatibility.Activate(result, () => called++, messages.Add);
        if (started != expected || called != (expected ? 1 : 0) || messages.Count != 1
            || (!expected && !messages[0].Contains("disabled")))
            throw new Exception("Compatibility result did not gate startup and log its reason.");
    }

    private static void Match() => Fixture((root, target) =>
        Verify(GameCompatibility.Check(root, target with { GameAssemblySha256 = target.GameAssemblySha256.ToLowerInvariant() }), true));
    private static void SteamBuild() => Fixture((_, target) =>
    {
        Verify(GameCompatibility.CheckSteamBuild("\"AppState\" { \"buildid\" \"1\" }", target), true);
        Verify(GameCompatibility.CheckSteamBuild("\"AppState\" { \"buildid\" \"2\" }", target), false);
        Verify(GameCompatibility.CheckSteamBuild("\"TargetBuildID\" \"1\"", target), false);
        Verify(GameCompatibility.CheckSteamBuild("", target), false);
    });
    private static void NativeMismatch() => Fixture((root, target) =>
    {
        File.AppendAllText(Path.Combine(root, "GameAssembly.dll"), "hotfix");
        Verify(GameCompatibility.Check(root, target), false);
    });
    private static void MetadataMismatch() => Fixture((root, target) =>
    {
        File.AppendAllText(Path.Combine(root, GameCompatibility.MetadataPath), "hotfix");
        Verify(GameCompatibility.Check(root, target), false);
    });
    private static void Missing() => Fixture((root, target) =>
    {
        using (File.Open(Path.Combine(root, "GameAssembly.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Verify(GameCompatibility.Check(root, target), false);
        File.Delete(Path.Combine(root, GameCompatibility.MetadataPath));
        Verify(GameCompatibility.Check(root, target), false);
    });
    private static void Invalid() => Fixture((root, target) =>
    {
        foreach (var invalid in new TargetBuild?[] { null, target with { SchemaVersion = 2 },
            target with { GameAssemblySha256 = "" }, target with { MetadataSha256 = new string('z', 64) },
            target with { GameVersion = "" } })
            Verify(GameCompatibility.Check(root, invalid), false);
    });
    private static void Embedded() => Fixture((root, _) =>
    {
        var result = GameCompatibility.CheckInstalled(root);
        Verify(result, false);
        if (!result.Message.Contains("unsupported game binaries"))
            throw new Exception("Embedded manifest was not read successfully.");
    });
}
