using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MechaCommunityMod.Configuration;

namespace MechaCommunityMod.Launcher;

internal static class Program
{
    internal static void Launch(GameLaunch plan)
    {
        // Use Steam's executable so the per-launch Doorstop disable flag is
        // passed to Mechabellum. Do not change persistent loader configuration.
        var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
        if (string.IsNullOrWhiteSpace(steam) || !File.Exists(steam))
            throw new FileNotFoundException("Could not locate Steam to launch Mechabellum.");
        var start = new ProcessStartInfo(steam) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in plan.SteamArguments) start.ArgumentList.Add(argument);
        Process.Start(start);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Length == 2 && args[0] == "--validate-defaults") { SettingsCatalog.ReadDefaults(args[1]); return 0; }
            var game = new Installation(Path.Combine(AppContext.BaseDirectory, "..", ".."));
            game.Validate(args.FirstOrDefault() != "--uninstall-cleanup");
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "--initialize": game.Initialize(args.Contains("--owns-loader")); return 0;
                    case "--apply" when args.Length == 2:
                        game.Apply(JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1])))
                            ?? throw new InvalidDataException("No settings supplied.")); return 0;
                    case "--uninstall-cleanup": game.Cleanup(args.Contains("--remove-stats")); return 0;
                    default: throw new InvalidDataException("Unknown launcher command.");
                }
            }
            Application.Run(new LauncherForm(game)); return 0;
        }
        catch (Exception error) { MessageBox.Show(error.Message, ModIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }

    internal static void Save(Installation game, Dictionary<string, string> values)
    {
        try { game.Apply(values); }
        catch (UnauthorizedAccessException)
        {
            var settings = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));
            var request = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            request.ArgumentList.Add("--apply"); request.ArgumentList.Add(settings);
            using var process = Process.Start(request) ?? throw new IOException("Could not save settings.");
            process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("Settings were not saved.");
        }
    }
}
