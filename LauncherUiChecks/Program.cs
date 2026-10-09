using MechaCommunityMod.Configuration;
using MechaCommunityMod.Launcher;
using System.Drawing.Imaging;

internal static class UiChecks
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("Launcher UI check failed: " + error);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../output/launcher-review"));
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(directory, "preview-game");
        Directory.CreateDirectory(Path.Combine(fixture, "Mechabellum_Data"));
        Directory.CreateDirectory(Path.Combine(fixture, "MechaCommunityMod"));
        File.WriteAllText(Path.Combine(fixture, "Mechabellum.exe"), "fixture");
        File.WriteAllText(Path.Combine(fixture, "GameAssembly.dll"), "fixture");
        using var form = new LauncherForm(new Installation(fixture, () => { }),
            _ => Task.FromResult<ReleaseUpdate?>(new("v9.0.0", "MechaCommunityMod-9.0.0-Setup.exe")));
        // Create real child handles without displaying a window to the user.
        form.Opacity = 0; form.ShowInTaskbar = false;
        form.Show(); Application.DoEvents(); form.PerformLayout();
        var layout = form.Controls.OfType<TableLayoutPanel>().Single();
        var actions = layout.Controls.OfType<FlowLayoutPanel>().First();
        var launch = actions.Controls.OfType<Button>().Last();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!layout.Controls.OfType<Label>().Any(l => l.Text.Contains("Wait for a newer")) && timeout.Elapsed < TimeSpan.FromSeconds(5))
        { Application.DoEvents(); Thread.Sleep(10); }
        if (!launch.Enabled || launch.Text != "Play vanilla")
            throw new Exception("Asynchronous compatibility check did not finish in vanilla mode for the missing game.");
        if (!layout.Controls.OfType<Label>().Any(l => l.Text.Contains("Wait for a newer Mecha Community Mod build")))
            throw new Exception("Unsupported build message is missing.");
        // Exercise native window discovery without activating another application.
        form.ShowInTaskbar = true; Application.DoEvents();
        if (GameWindow.FindWindow(Environment.ProcessId) != form.Handle)
            throw new Exception("Game window lookup could not find a visible top-level window.");
        form.WindowState = FormWindowState.Minimized; Application.DoEvents();
        if (GameWindow.FindWindow(Environment.ProcessId) != form.Handle)
            throw new Exception("Game window lookup cannot restore a minimized window.");
        form.WindowState = FormWindowState.Normal; Application.DoEvents();
        form.Hide(); Application.DoEvents();
        if (GameWindow.FindWindow(Environment.ProcessId) != IntPtr.Zero)
            throw new Exception("Game window lookup accepted an invisible window.");
        form.Show(); Application.DoEvents();
        var tabs = layout.Controls.OfType<TabControl>().Single();
        if (string.Join(",", tabs.TabPages.Cast<TabPage>().Select(p => p.Text)) != "Plugins,Live overlay,Live unit stats,Post-game stats")
            throw new Exception("Independent table settings tabs are missing.");
        var unitOptions = tabs.TabPages[2].Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().ToArray();
        var postOptions = tabs.TabPages[3].Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().ToArray();
        if (unitOptions.Any(c => c.Text == "Individual units / squads") || !postOptions.Any(c => c.Text == "Individual units / squads"))
            throw new Exception("Grouping must only be offered for post-game stats.");
        var liveDamage = unitOptions.Single(c => c.Text == "Damage");
        var postDamage = postOptions.Single(c => c.Text == "Damage");
        var previousPostDamage = postDamage.Checked;
        liveDamage.Checked = !liveDamage.Checked;
        if (postDamage.Checked != previousPostDamage) throw new Exception("Live and post-game checkboxes are coupled.");
        var saved = new Installation(Path.Combine(directory, "preview-game")).ReadSettings();
        if (bool.Parse(saved["LiveUnitStatistics/DamageDealt"]) != liveDamage.Checked
            || bool.Parse(saved["Statistics/DamageDealt"]) != previousPostDamage)
            throw new Exception("Checkbox changes were not saved immediately and independently.");
        // Exercise layout with an available update as well as Defaults.
        if (!actions.Controls.OfType<Button>().Single(b => b.Text == "Update available").Visible)
            throw new Exception("Available launcher update was not shown.");
        foreach (TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page; Application.DoEvents(); form.PerformLayout();
            if (actions.Controls.Cast<Control>().Where(c => c.Visible).Any(c => c.Right > actions.ClientSize.Width))
                throw new Exception("Action buttons do not fit on " + page.Text);
            if (layout.Controls.Cast<Control>().Any(c => c.Bottom > layout.ClientSize.Height))
                throw new Exception("Launcher content clips on " + page.Text);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, page.Text.Replace(' ', '-') + ".png"), ImageFormat.Png);
        }
        tabs.SelectedIndex = 0; Application.DoEvents();
        var pluginList = tabs.TabPages[0].Controls.OfType<FlowLayoutPanel>().Single();
        if (pluginList.Controls.OfType<CheckBox>().Count() != 3
            || pluginList.Controls.OfType<Label>().Any(l => l.Text.Contains('\n')))
            throw new Exception("Plugin selection contains explanatory paragraphs or missing checkboxes.");
        if (pluginList.Controls.Cast<Control>().Any(c => c.Bottom > pluginList.ClientSize.Height))
            throw new Exception("Plugin checkboxes do not fit in the compact window.");
        if (string.Join(",", actions.Controls.OfType<Button>().Where(b => b.Visible).Select(b => b.Text)) != "Uninstall,Update available,Play,Play vanilla")
            throw new Exception("Unexpected primary launcher actions.");
        var buttons = layout.Controls.OfType<FlowLayoutPanel>().First();
        if (buttons.Controls.Cast<Control>().Where(c => c.Visible).Any(c => c.Right > buttons.ClientSize.Width)) throw new Exception("Action buttons do not fit.");
        if (layout.Controls.Cast<Control>().Any(c => c.Bottom > layout.ClientSize.Height)) throw new Exception("Launcher content clips at the bottom.");
        Console.WriteLine("Async build verification, native window discovery, launcher tabs and action layout verified. Previews: " + directory);
        return 0;
    }
}

