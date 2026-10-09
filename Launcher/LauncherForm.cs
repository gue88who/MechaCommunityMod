using System.Diagnostics;
using MechaCommunityMod.Configuration;

namespace MechaCommunityMod.Launcher;

internal sealed class LauncherForm : Form
{
    private readonly Installation _game;
    private readonly Dictionary<string, CheckBox> _inputs = new();
    private readonly Label _status = new();
    private readonly TabControl _tabs;
    private readonly List<Button> _buttons = new();
    private readonly Button _launchButton;
    private readonly Button _vanillaButton;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;
    private bool _checking = true;
    private bool _modEnabled;
    private bool _gameStarted;
    private bool _populating;
    private Dictionary<string, string> _savedValues = new();

    internal LauncherForm(Installation game)
    {
        _game = game;
        Text = ModIdentity.Name;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        _tabs = new TabControl { Dock = DockStyle.Fill };
        layout.Controls.Add(_tabs);
        foreach (var group in new[] { ("Features", "Plugins"), ("Overlay", "Live overlay"),
            ("UnitStats", "Live unit stats"), ("Stats", "Post-game stats") })
        {
            var page = new TabPage(group.Item2) { Padding = new Padding(12), BackColor = SystemColors.Window };
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            page.Controls.Add(list); _tabs.TabPages.Add(page);
            if (group.Item1 == "Features") list.Controls.Add(new Label {
                Text = "Battle Statistics", AutoSize = true, Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(2, 4, 2, 12) });
            foreach (var definition in SettingsCatalog.All.Where(s => s.Group == group.Item1))
            {
                var checkbox = new CheckBox { Text = definition.Label, AutoSize = true, Margin = new Padding(2, 4, 2, 6) };
                _inputs.Add(definition.Id, checkbox); list.Controls.Add(checkbox);
            }
        }
        var values = game.ReadSettings(); Populate(values); _savedValues = values;
        foreach (var checkbox in _inputs.Values)
            checkbox.CheckedChanged += (_, _) => { if (!_populating && !_busy) ApplySettings(); };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 12, 0, 6) };
        actions.Controls.Add(ActionButton("Uninstall", Uninstall));
        var defaults = ActionButton("Defaults", () => { Populate(SettingsCatalog.ReadDefaults(_game.DefaultsPath)); ApplySettings(); });
        defaults.Visible = false;
        actions.Controls.Add(defaults);
        _launchButton = CreateButton("Play");
        _launchButton.Enabled = false;
        _vanillaButton = CreateButton("Play vanilla");
        _vanillaButton.Click += async (_, _) => await StartLaunch(true);
        _launchButton.Click += async (_, _) => await StartLaunch(false);
        actions.Controls.Add(_launchButton);
        actions.Controls.Add(_vanillaButton);
        layout.Controls.Add(actions);
        _status.AutoSize = true; _status.MaximumSize = new Size(520, 0); _status.ForeColor = SystemColors.GrayText;
        _status.Margin = new Padding(0, 6, 0, 0); _status.Text = "Checking game build…";
        layout.Controls.Add(_status);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            defaults.Visible = _tabs.SelectedIndex != 0;
            ClientSize = LogicalToDeviceUnits(new Size(560, _tabs.SelectedIndex == 0 ? 340 : 630));
        };
        Shown += async (_, _) =>
        {
            var plan = await Task.Run(() => GameLaunch.For(_game.GameDirectory));
            if (IsDisposed || _closing.IsCancellationRequested) return;
            _modEnabled = plan.ModEnabled;
            _checking = false;
            SetBusy(_busy);
            ShowStatus("");
        };
        FormClosed += (_, _) => _closing.Cancel();
    }
    private async Task StartLaunch(bool vanilla)
        {
            if (_busy) return;
            SetBusy(true);
            try { await LaunchGame(vanilla); }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!IsDisposed) MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { if (!IsDisposed) SetBusy(false); }
        }
    private Button CreateButton(string text)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        _buttons.Add(button);
        return button;
    }
    private Button ActionButton(string text, Action action)
    {
        var button = CreateButton(text);
        button.Click += (_, _) => { if (_busy) return; try { action(); } catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        return button;
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        _tabs.Enabled = !busy;
        foreach (var button in _buttons) button.Enabled = !busy;
        _launchButton.Enabled = !busy && !_checking && _modEnabled && !_gameStarted;
        _vanillaButton.Enabled = !busy && !_gameStarted;
        UseWaitCursor = busy;
    }
    private void Populate(Dictionary<string, string> values)
    {
        _populating = true;
        try
        {
            foreach (var (id, checkbox) in _inputs)
                checkbox.Checked = bool.Parse(values[id]);
        }
        finally { _populating = false; }
    }
    private void ApplySettings()
    {
        var values = _inputs.ToDictionary(p => p.Key, p => p.Value.Checked.ToString().ToLowerInvariant());
        SetBusy(true);
        try
        {
            Program.Save(_game, values);
            _savedValues = values;
            ShowStatus("Settings apply on the next game launch.");
        }
        catch (Exception error)
        {
            Populate(_savedValues);
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }
    private void ShowStatus(string message) => _status.Text = !_checking && !_modEnabled
        ? "Unsupported game build. Wait for a newer Mecha Community Mod build."
            + (message.Length == 0 ? "" : Environment.NewLine + message)
        : message;
    private async Task LaunchGame(bool vanilla)
    {
        if (!_gameStarted)
        {
            Installation.RequireGameClosed();
            _status.Text = "Checking game build…";
            // One fresh check before launching,
            // off the UI thread. The initial screen check is never trusted to launch.
            var plan = vanilla ? new GameLaunch(false, "Vanilla launch requested.")
                : await Task.Run(() => GameLaunch.For(_game.GameDirectory));
            _closing.Token.ThrowIfCancellationRequested();
            if (!vanilla && !plan.ModEnabled) MessageBox.Show(this, plan.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Program.Launch(plan);
            _gameStarted = true;
        }
        _launchButton.Text = "Launching…";
        _status.Text = "Starting Mechabellum…";
        var result = await GameWindow.WaitAndActivate(_closing.Token);
        if (result != GameWindowResult.TimedOut) { Close(); return; }
        _launchButton.Text = "Launch requested";
        _status.Text = "Mechabellum hasn't opened yet. Check Steam.";
    }
    private void Uninstall()
    {
        Installation.RequireGameClosed();
        if (!File.Exists(_game.Uninstaller)) throw new FileNotFoundException("The uninstaller is missing. Run Setup again to repair this installation.");
        using var options = new Form { Text = "Uninstall", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, Font = Font, AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(380, 130) };
        var removeStats = new CheckBox { Text = "Delete saved match stats", AutoSize = true, Location = new Point(20, 20) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(160, 76), Size = new Size(90, 32) };
        var confirm = new Button { Text = "Uninstall", DialogResult = DialogResult.OK, Location = new Point(260, 76), Size = new Size(100, 32) };
        options.Controls.AddRange(new Control[] { removeStats, cancel, confirm });
        options.AcceptButton = confirm; options.CancelButton = cancel;
        if (options.ShowDialog(this) != DialogResult.OK) return;
        var start = new ProcessStartInfo(_game.Uninstaller) { UseShellExecute = true };
        if (removeStats.Checked) start.ArgumentList.Add("/REMOVESTATS");
        Process.Start(start); Close();
    }
}
