namespace MechaCommunityMod.Launcher;

internal sealed partial class LauncherForm
{
    private DialogResult ConfirmUntestedGame(string message)
    {
        using var warning = new Form {
            Name = "UntestedGameWarning", Text = "Untested game version",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, Font = Font, AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = LauncherSkin.Surface, ForeColor = LauncherSkin.Text,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        var layout = new TableLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3
        };
        layout.Controls.Add(new Label {
            Text = "This game version has not been tested yet", AutoSize = true,
            ForeColor = LauncherSkin.Gold, Font = _skin.Button, MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 16)
        });
        layout.Controls.Add(new Label {
            Text = message, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 20)
        });
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var mod = new GameButton(_skin) { Text = "Play with mod", DialogResult = DialogResult.Yes, Primary = true };
        var vanilla = new GameButton(_skin) { Text = "Play vanilla", DialogResult = DialogResult.No };
        var back = new GameButton(_skin) { Text = "Go back", DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange(new Control[] { mod, vanilla, back });
        layout.Controls.Add(actions);
        warning.Controls.Add(layout);
        warning.AcceptButton = back; warning.CancelButton = back;
        return warning.ShowDialog(this);
    }
}
