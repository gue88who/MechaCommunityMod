# Mecha Community Mod

Adds independently enabled features to Mechabellum:

- **Live battle stats:** an overlay showing your armies' combat stats.
- **Individual unit stats:** detailed stats for the selected unit or squad.
- **Post-game stats:** a match report with round history and damage breakdowns.
- **Beacon selection preview:** highlights units affected before placement.
- **Sentry missile range preview:** highlights targets within the missile's range.

Both targeting previews are disabled by default. The launcher's **Fair play** guide
explains each feature's competitive impact. Follow developer and event rules.

## Install

1. Download the Setup EXE from [Releases](https://github.com/gue88who/MechaCommunityMod/releases).
2. Close Mechabellum, run Setup, and choose your game folder and features.
3. Start the game through Steam or the included **Mecha Community Mod Launcher**.

Everything required is bundled. The first game start may take longer.

**Supported:** Windows x64, Mechabellum **2.0.0.5.2340** (Steam build **26153644**).
Untested game builds show a warning with **Play with mod**, **Play vanilla**, and
**Go back** choices. Features may not work correctly on untested builds.

## Controls

- **F8 (default):** toggle or hold to show the overlay; configure the key in Settings.
- **Overlay Settings:** choose sidebars or a centered scoreboard and visibility mode separately for battle and deployment.
- **Selected-unit Stats button:** open unit stats, above the camera button.
- **Stats on the results screen:** open the match report.
- **Settings:** choose the stats shown. **?** explains them.

In-game display changes save and apply immediately. Launcher changes apply next
game launch. Each feature has its own settings page and Defaults button.

Play or watch replays from the beginning to capture the full match. Reports save
automatically in `Mechabellum/ProjectDatas/Stats`. The mod does not change combat results.

## Update or uninstall

Close the game before updating. Click **Update available** in the launcher or Setup
to download, verify, and run the latest installer. You can also run a newer Setup
directly. Your saved settings are kept.

Use **Uninstall** in the launcher or Windows Installed Apps to remove the mod.
Game files and replays are kept. Saved reports are kept unless you choose to delete them.

## Problems?

Include your game version and a description of the problem in an
[issue](https://github.com/gue88who/MechaCommunityMod/issues). Logs are in
`BepInEx/LogOutput.log`; check them for personal information before sharing.
