namespace MechaCommunityMod.Configuration;

internal sealed record GameLaunch(bool ModEnabled, string Message)
{
    internal string[] SteamArguments => ModEnabled
        ? new[] { "-applaunch", "669330" }
        : new[] { "-applaunch", "669330", "--doorstop-enabled", "false" };

    internal static GameLaunch For(string gameDirectory) => From(GameCompatibility.CheckInstalled(gameDirectory));
    internal static GameLaunch From(CompatibilityResult result) => result.Compatible
        ? new(true, "Supported game build. Mecha Community Mod is ready.")
        : new(false, "This Mechabellum build is not supported. Wait for a newer Mecha Community Mod build. "
            + "The game will launch in vanilla mode, without the mod.");
}
