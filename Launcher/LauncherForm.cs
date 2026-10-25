using System.Diagnostics;
using MechaCommunityMod.Configuration;

namespace MechaCommunityMod.Launcher;

internal sealed partial class LauncherForm : Form
{
    private readonly LauncherSkin _skin = new();
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_bindingHotkey && (keyData & Keys.KeyCode) == Keys.Tab)
        {
            _hotkey = "Tab";
            _bindingHotkey = false;
            _hotkeyButton!.Text = _hotkey;
            ApplySettings();
            return true;
        }
        if (!_bindingHotkey && keyData == Keys.Escape && _selectedPage is not null)
        { ShowPage(null); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private readonly Installation _game;
    private readonly Func<GameLaunch> _checkGame;
    private readonly Action<GameLaunch> _launchGame;
    private readonly Func<CancellationToken, Task<GameWindowResult>> _waitForGame;
    private readonly Dictionary<string, CheckBox> _inputs = new();
    private readonly Dictionary<string, ComboBox> _choices = new();
    private readonly Dictionary<string, NumericUpDown> _numbers = new();
    private Button? _hotkeyButton;
    private string _hotkey = "F8";
    private bool _bindingHotkey;
    private readonly Label _status = new();
    private readonly ToolTip _fairnessTips = new();
    private readonly List<Button> _buttons = new();
    private readonly Button _launchButton;
    private readonly Button _vanillaButton;
    private readonly Button _updateButton;
    private ReleaseUpdate? _update;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;
    private bool _checking = true;
    private bool _requiresWarning;
    private bool _gameStarted;
    private bool _populating;
    private Dictionary<string, string> _savedValues = new();

    internal LauncherForm(Installation game, Func<CancellationToken, Task<ReleaseUpdate?>>? checkUpdate = null,
        Func<GameLaunch>? checkGame = null, Action<GameLaunch>? launchGame = null,
        Func<CancellationToken, Task<GameWindowResult>>? waitForGame = null)
    {
        _game = game;
        _checkGame = checkGame ?? (() => GameLaunch.For(_game.GameDirectory));
        _launchGame = launchGame ?? Program.Launch;
        _waitForGame = waitForGame ?? GameWindow.WaitAndActivate;
        Text = $"{ModIdentity.Name} {ModIdentity.Version}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 700);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = _skin.Body; AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = LauncherSkin.Background; ForeColor = LauncherSkin.Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 20, 18), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new GameBanner(_skin) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) });
        BuildNavigation(layout);
        var values = game.ReadSettings(); Populate(values); _savedValues = values;
        KeyPreview = true;
        KeyDown += (_, key) =>
        {
            if (!_bindingHotkey) return;
            key.Handled = true; key.SuppressKeyPress = true;
            if (key.KeyCode == Keys.Escape) { _bindingHotkey = false; _hotkeyButton!.Text = _hotkey; return; }
            var name = key.KeyCode.ToString();
            name = key.KeyCode switch {
                Keys.Up => "UpArrow", Keys.Down => "DownArrow", Keys.Left => "LeftArrow", Keys.Right => "RightArrow",
                Keys.Back => "Backspace", Keys.ShiftKey => "LeftShift", Keys.ControlKey => "LeftControl", Keys.Menu => "LeftAlt",
                Keys.Capital => "CapsLock", Keys.NumLock => "Numlock", Keys.Scroll => "ScrollLock", Keys.PrintScreen => "Print",
                Keys.Oemtilde => "BackQuote", Keys.OemMinus => "Minus", Keys.Oemplus => "Equals", Keys.OemOpenBrackets => "LeftBracket",
                Keys.OemCloseBrackets => "RightBracket", Keys.OemPipe => "Backslash", Keys.OemSemicolon => "Semicolon", Keys.OemQuotes => "Quote",
                Keys.Oemcomma => "Comma", Keys.OemPeriod => "Period", Keys.OemQuestion => "Slash",
                Keys.Decimal => "KeypadPeriod", Keys.Divide => "KeypadDivide", Keys.Multiply => "KeypadMultiply", Keys.Subtract => "KeypadMinus", Keys.Add => "KeypadPlus",
                _ => name };
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = "Alpha" + name[1];
            if (name.StartsWith("NumPad")) name = "Keypad" + name.Substring(6);
            if (OverlayKeyPolicy.Reserved(name))
            { _hotkeyButton!.Text = "Reserved; press another letter or F-key"; return; }
            if (!SettingsCatalog.All.Single(s => s.Id == "Display/ToggleKey").Valid(name)) return;
            _hotkey = name; _bindingHotkey = false; _hotkeyButton!.Text = name; ApplySettings();
        };
        foreach (var checkbox in _inputs.Values)
            checkbox.CheckedChanged += (_, _) => { if (!_populating && !_busy) ApplySettings(); };
        foreach (var select in _choices.Values)
            select.SelectedIndexChanged += (_, _) => { if (!_populating && !_busy) ApplySettings(); };
        foreach (var number in _numbers.Values)
            number.ValueChanged += (_, _) => { if (!_populating && !_busy) ApplySettings(); };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 12, 0, 6) };
        actions.Controls.Add(ActionButton("Uninstall", Uninstall));
        _updateButton = CreateButton("Update available");
        _updateButton.Visible = false;
        _updateButton.Click += async (_, _) => await InstallUpdate();
        actions.Controls.Add(_updateButton);
        _launchButton = CreateButton("Play");
        ((GameButton)_launchButton).Primary = true;
        _launchButton.Enabled = false;
        _vanillaButton = CreateButton("Play vanilla");
        _vanillaButton.Click += async (_, _) => await StartLaunch(true);
        _launchButton.Click += async (_, _) => await StartLaunch(false);
        actions.Controls.Add(_launchButton);
        actions.Controls.Add(_vanillaButton);
        layout.Controls.Add(actions);
        _status.AutoSize = true; _status.MaximumSize = new Size(770, 0); _status.ForeColor = LauncherSkin.Muted; _status.Font = _skin.Small;
        _status.Margin = new Padding(0, 6, 0, 0); _status.Text = "Checking game build…";
        layout.Controls.Add(_status);
        Shown += (_, _) => ResizeForPage();
        Shown += async (_, _) =>
        {
            var plan = await Task.Run(_checkGame);
            if (IsDisposed || _closing.IsCancellationRequested) return;
            _requiresWarning = plan.RequiresWarning;
            _checking = false;
            SetBusy(_busy);
            ShowStatus("");
        };
        Shown += async (_, _) =>
        {
            try
            {
                var update = await (checkUpdate is null ? ReleaseUpdate.Check(ModIdentity.Version, _closing.Token) : checkUpdate(_closing.Token));
                if (IsDisposed || _closing.IsCancellationRequested) return;
                _update = update;
                _updateButton.Visible = update is not null;
            }
            catch (Exception) { /* Offline update checks must not prevent playing. */ }
        };
        FormClosed += (_, _) => { _closing.Cancel(); _fairnessTips.Dispose(); };
    }
    private ComboBox Choice() => new GameChoice(_skin);
    private void ResizeForPage()
    {
        var requested = LogicalToDeviceUnits(new Size(820, _selectedPage is null ? 700 : 780));
        var available = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(Math.Min(requested.Width, available.Width - (Width - ClientSize.Width) - 24),
            Math.Min(requested.Height, available.Height - (Height - ClientSize.Height) - 24));
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { var dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
        catch (DllNotFoundException) { } // Wine can use its own window decoration.
        catch (EntryPointNotFoundException) { }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _skin.Dispose();
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
    private async Task InstallUpdate()
    {
        if (_busy || _update is null) return;
        SetBusy(true);
        try
        {
            Installation.RequireGameClosed();
            ShowStatus("Downloading update...");
            var path = await _update.Download(_closing.Token);
            Installation.RequireGameClosed();
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            start.ArgumentList.Add("/DIR=" + _game.GameDirectory);
            if (Process.Start(start) is null) throw new InvalidOperationException("Could not open the update installer.");
            Close();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!IsDisposed) { ShowStatus(""); MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private Button CreateButton(string text)
    {
        var button = new GameButton(_skin) { Text = text };
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
        _content.Enabled = !busy;
        foreach (var button in _buttons) button.Enabled = !busy;
        _launchButton.Enabled = !busy && !_checking && !_gameStarted;
        _vanillaButton.Enabled = !busy && !_gameStarted;
        UseWaitCursor = busy;
    }
    private void Populate(Dictionary<string, string> values)
    {
        _populating = true;
        try
        {
            _hotkey = values["Display/ToggleKey"]; if (_hotkeyButton is not null) _hotkeyButton.Text = _hotkey;
            foreach (var (id, checkbox) in _inputs)
                checkbox.Checked = bool.Parse(values[id]);
            foreach (var (id, number) in _numbers) number.Value = decimal.Parse(values[id], System.Globalization.CultureInfo.InvariantCulture);
            foreach (var (id, select) in _choices)
                if (bool.TryParse(values[id], out var enabled)) select.SelectedIndex = enabled ? 1 : 0;
                else select.SelectedItem = values[id];
        }
        finally { _populating = false; }
    }
    private void ApplySettings()
    {
        var values = _inputs.ToDictionary(p => p.Key, p => p.Value.Checked.ToString().ToLowerInvariant());
        values["Display/ToggleKey"] = _hotkey;
        foreach (var (id, number) in _numbers) values[id] = number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (id, select) in _choices) values[id] = id == "Display/ToggleKey" ? (string)select.SelectedItem! : (select.SelectedIndex == 1).ToString().ToLowerInvariant();
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
    private void ShowStatus(string message) => _status.Text = !_checking && _requiresWarning
        ? "This game version has not been tested yet. You can still play with the mod."
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
                : await Task.Run(_checkGame);
            _closing.Token.ThrowIfCancellationRequested();
            if (!vanilla)
            {
                _requiresWarning = plan.RequiresWarning;
                if (plan.RequiresWarning)
                {
                    var choice = ConfirmUntestedGame(plan.Message);
                    if (choice == DialogResult.Cancel) { ShowStatus(""); return; }
                    if (choice == DialogResult.No) plan = new GameLaunch(false, "Vanilla launch requested.");
                }
            }
            _launchGame(plan);
            _gameStarted = true;
        }
        _launchButton.Text = "Launching…";
        _status.Text = "Starting Mechabellum…";
        var result = await _waitForGame(_closing.Token);
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
            ShowInTaskbar = false, Font = Font, AutoScaleMode = AutoScaleMode.Dpi, BackColor = LauncherSkin.Surface,
            ForeColor = LauncherSkin.Text, ClientSize = new Size(420, 160) };
        var removeStats = new GameCheckBox(_skin) { Text = "Delete saved match stats", Location = new Point(20, 20) };
        var cancel = new GameButton(_skin) { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(158, 96) };
        var confirm = new GameButton(_skin) { Text = "Uninstall", DialogResult = DialogResult.OK, Location = new Point(282, 96) };
        options.Controls.AddRange(new Control[] { removeStats, cancel, confirm });
        options.AcceptButton = confirm; options.CancelButton = cancel;
        if (options.ShowDialog(this) != DialogResult.OK) return;
        var start = new ProcessStartInfo(_game.Uninstaller) { UseShellExecute = true };
        if (removeStats.Checked) start.ArgumentList.Add("/REMOVESTATS");
        Process.Start(start); Close();
    }
}
