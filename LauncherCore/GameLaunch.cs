namespace MechaCommunityMod.Configuration;

internal sealed record GameLaunch(bool ModEnabled, string Message, bool RequiresWarning = false)
{
    internal string[] SteamArguments => ModEnabled
        ? new[] { "-applaunch", "669330" }
        : new[] { "-applaunch", "669330", "--doorstop-enabled", "false" };

    internal static GameLaunch For(string gameDirectory) => From(GameCompatibility.CheckInstalled(gameDirectory));
    internal static GameLaunch From(CompatibilityResult result) => result.Compatible
        ? new(true, "Supported game build. Mecha Community Mod is ready.")
        : new(true, "This version of Mechabellum has not been tested with this version of Mecha Community Mod. "
            + "The mod may not work correctly. You can play with the mod, play vanilla, or go back.", true);
}
