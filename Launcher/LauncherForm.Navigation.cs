using MechaCommunityMod.Configuration;

namespace MechaCommunityMod.Launcher;

internal sealed partial class LauncherForm
{
    private readonly TableLayoutPanel _content = new();
    private readonly Panel _pageHost = new();
    private readonly Label _pageTitle = new();
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly Dictionary<string, string[]> _featureSettings = new();
    private Button _backButton = null!, _defaultsButton = null!, _guideButton = null!;
    private string? _selectedPage;
    private const string HomePage = "Plugins";
    private const string GuidePage = "FairPlay";

    private void BuildNavigation(TableLayoutPanel layout)
    {
        _content.Name = "Content"; _content.Dock = DockStyle.Fill;
        _content.Margin = Padding.Empty; _content.ColumnCount = 1; _content.RowCount = 2;
        _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_content);
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, 8) };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _backButton = CreateButton("Back"); _backButton.Name = "Back";
        _backButton.Click += (_, _) => ShowPage(null);
        _pageTitle.AutoSize = true; _pageTitle.Font = _skin.Button;
        _pageTitle.ForeColor = LauncherSkin.Gold; _pageTitle.Anchor = AnchorStyles.Left;
        _pageTitle.Margin = new Padding(4, 0, 0, 0);
        var context = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _guideButton = CreateButton("Fair play"); _guideButton.Name = GuidePage;
        _guideButton.Click += (_, _) => ShowPage(GuidePage);
        _defaultsButton = CreateButton("Defaults"); _defaultsButton.Name = "FeatureDefaults";
        _defaultsButton.Click += (_, _) => RestoreFeatureDefaults();
        context.Controls.AddRange(new Control[] { _guideButton, _defaultsButton });
        navigation.Controls.Add(_backButton, 0, 0); navigation.Controls.Add(_pageTitle, 1, 0); navigation.Controls.Add(context, 2, 0);
        _content.Controls.Add(navigation, 0, 0);
        _pageHost.Name = "Pages"; _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = LauncherSkin.Surface; _pageHost.Padding = new Padding(14);
        _pageHost.Margin = Padding.Empty;
        _pageHost.Paint += (_, e) => _skin.Draw(e.Graphics, "panel_blue_70_with_corners", _pageHost.ClientRectangle);
        _content.Controls.Add(_pageHost, 0, 1);

        var home = Page(HomePage);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        home.Controls.Add(scroll);
        var table = new TableLayoutPanel { Name = "PluginTable", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, BackColor = LauncherSkin.Surface, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        scroll.Controls.Add(table);
        var row = 0;
        AddHeader("", "Feature", "", "");
        foreach (var group in new[] { ("Features", "Battle Statistics"), ("VisualHelpers", "Visual Helpers") })
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var heading = TextLabel(group.Item2, _skin.Button, LauncherSkin.Gold);
            table.Controls.Add(heading, 0, row++); table.SetColumnSpan(heading, 4);
            foreach (var feature in SettingsCatalog.All.Where(s => s.Section == "Features" && s.Group == group.Item1))
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
                var rating = FeatureFairness.For(feature.Id);
                var toggle = new GameCheckBox(_skin) { Name = feature.Id, Text = "", AutoSize = false,
                    Size = new Size(28, 36), Anchor = AnchorStyles.Left, AccessibleName = "Enable " + feature.Label,
                    AccessibleDescription = rating.Reason, Margin = new Padding(6, 0, 0, 0) };
                _inputs.Add(feature.Id, toggle);
                var name = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 4, 4, 4) };
                name.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); name.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
                name.Controls.Add(TextLabel(feature.Label, _skin.Body, LauncherSkin.Text));
                name.Controls.Add(TextLabel(FeatureSummary(feature.Key), _skin.Small, LauncherSkin.Muted));
                var indicator = new FairnessIndicator { Name = "Fairness/" + feature.Id, ForeColor = LauncherSkin.Fairness(rating.Level),
                    AccessibleName = FeatureFairness.Definition(rating.Level).Title, AccessibleDescription = rating.Reason,
                    Anchor = AnchorStyles.Left, Margin = Padding.Empty };
                _fairnessTips.SetToolTip(indicator, indicator.AccessibleName + ": " + rating.Reason);
                var settings = new GameButton(_skin) { Name = "Settings/" + feature.Id, SettingsIcon = true,
                    Anchor = AnchorStyles.Left, AccessibleName = feature.Label + " settings" };
                _fairnessTips.SetToolTip(settings, feature.Label + " settings");
                settings.Click += (_, _) => ShowPage(feature.Id);
                _buttons.Add(settings);
                table.Controls.Add(toggle, 0, row); table.Controls.Add(name, 1, row);
                table.Controls.Add(indicator, 2, row); table.Controls.Add(settings, 3, row++);
                BuildFeaturePage(feature);
            }
        }
        table.RowCount = row;
        table.CellPaint += (_, e) => { if (e.Row > 0) { using var line = new Pen(Color.FromArgb(38, 49, 72)); e.Graphics.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1); } };
        BuildGuide();
        ShowPage(null);

        void AddHeader(params string[] captions)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            for (var column = 0; column < captions.Length; column++) table.Controls.Add(TextLabel(captions[column], _skin.Small, LauncherSkin.Muted), column, row);
            row++;
        }
    }

    private Panel Page(string key)
    {
        var page = new Panel { Name = key, Dock = DockStyle.Fill, Visible = false, BackColor = LauncherSkin.Surface, Margin = Padding.Empty };
        _pages.Add(key, page); _pageHost.Controls.Add(page); return page;
    }

    private static Label TextLabel(string text, Font font, Color color) => new() {
        Text = text, Font = font, ForeColor = color, AutoSize = false, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(2, 0, 2, 0)
    };

    private static string FeatureSummary(string key) => key switch {
        "LiveOverlay" => "Army totals during battle",
        "IndividualStats" => "Detailed statistics for the selected squad",
        "PostGameStats" => "Reports for completed rounds and matches",
        "BeaconSelectionPreview" => "Highlights units affected before placement",
        _ => "Highlights targets inside the missile's range"
    };

    private void BuildFeaturePage(SettingDefinition feature)
    {
        var definitions = SettingsCatalog.All.Where(s => feature.Key switch {
            "LiveOverlay" => s.Group is "Overlay" or "Layout",
            "IndividualStats" => s.Group == "UnitStats",
            "PostGameStats" => s.Group == "Stats",
            _ => false
        }).ToArray();
        _featureSettings[feature.Id] = definitions.Select(s => s.Id).Prepend(feature.Id).ToArray();
        var page = Page(feature.Id);
        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        page.Controls.Add(list);
        var enabled = new GameCheckBox(_skin) { Name = "Enable/" + feature.Id, Text = "Enabled", Margin = new Padding(2, 0, 2, 8) };
        enabled.CheckedChanged += (_, _) => { if (!_populating) _inputs[feature.Id].Checked = enabled.Checked; };
        _inputs[feature.Id].CheckedChanged += (_, _) => enabled.Checked = _inputs[feature.Id].Checked;
        list.Controls.Add(enabled);
        var explanation = new Label { Text = FeatureFairness.For(feature.Id).Reason, AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = LauncherSkin.Muted, Font = _skin.Small, Margin = new Padding(2, 0, 2, 16) };
        list.Controls.Add(explanation);
        var options = new TableLayoutPanel { Name = "Options/" + feature.Id, AutoSize = true, ColumnCount = 2, Width = 680, Margin = new Padding(2) };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        list.Controls.Add(options);
        list.Resize += (_, _) => {
            options.MinimumSize = new Size(Math.Max(200, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8), 0);
            options.Width = options.MinimumSize.Width; explanation.MaximumSize = new Size(options.Width, 0);
        };
        var row = 0;
        foreach (var definition in definitions)
        {
            Control control;
            var label = definition.Label;
            if (definition.Minimum is { } minimum && definition.Maximum is { } maximum)
            {
                var number = new NumericUpDown { Name = definition.Id, Minimum = minimum, Maximum = maximum, DecimalPlaces = definition.Decimals,
                    Width = 120, BackColor = LauncherSkin.Background, ForeColor = LauncherSkin.Text, BorderStyle = BorderStyle.FixedSingle };
                _numbers.Add(definition.Id, number); control = number;
            }
            else if (definition.Id == "Display/ToggleKey")
            {
                _hotkeyButton = new GameButton(_skin) { Name = definition.Id, Text = _hotkey };
                _hotkeyButton.Click += (_, _) => { _bindingHotkey = true; _hotkeyButton.Text = "Press a key (Esc cancels)"; };
                control = _hotkeyButton;
            }
            else if (definition.Key is "BattleCentered" or "DeploymentCentered" or "BattleOnDemand" or "DeploymentOnDemand")
            {
                var battle = definition.Key.StartsWith("Battle"); var layout = definition.Key.EndsWith("Centered");
                label = (battle ? "Battle" : "Deployment") + (layout ? " layout" : " visibility");
                var select = Choice(); select.Name = definition.Id;
                select.Items.AddRange(layout ? new[] { "Sidebars", "Scoreboard" } : new[] { "Toggle", "Hold" });
                _choices.Add(definition.Id, select); control = select;
            }
            else if (definition.Choices is { } choices)
            {
                var select = Choice(); select.Name = definition.Id; select.Items.AddRange(choices);
                _choices.Add(definition.Id, select); control = select;
            }
            else
            {
                var check = new GameCheckBox(_skin) { Name = definition.Id, Text = label, Margin = new Padding(2, 0, 2, 2) };
                _inputs.Add(definition.Id, check);
                options.RowStyles.Add(new RowStyle(SizeType.AutoSize)); options.Controls.Add(check, 0, row++); options.SetColumnSpan(check, 2);
                continue;
            }
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = TextLabel(label, _skin.Body, LauncherSkin.Text); caption.MinimumSize = new Size(0, 36);
            control.Margin = new Padding(2, 2, 2, 8); control.Anchor = AnchorStyles.Left;
            options.Controls.Add(caption, 0, row); options.Controls.Add(control, 1, row++);
        }
        options.RowCount = row;
    }

    private void BuildGuide()
    {
        var page = Page(GuidePage);
        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        page.Controls.Add(list);
        list.Controls.Add(new Label { Text = FeatureFairness.Disclaimer, AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(2, 4, 2, 16) });
        foreach (var level in FeatureFairness.Levels)
        {
            list.Controls.Add(new Label { Text = level.Title, AutoSize = true, ForeColor = LauncherSkin.Fairness(level.Level), Font = _skin.Button, Margin = new Padding(2, 8, 2, 4) });
            list.Controls.Add(new Label { Text = level.Description, AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(2, 0, 2, 12) });
        }
    }

    private void ShowPage(string? key)
    {
        _bindingHotkey = false;
        if (_hotkeyButton is not null) _hotkeyButton.Text = _hotkey;
        _selectedPage = key;
        foreach (var page in _pages) page.Value.Visible = page.Key == (key ?? HomePage);
        _backButton.Visible = key is not null;
        _guideButton.Visible = key is null;
        _defaultsButton.Visible = key is not null && key != GuidePage;
        _pageTitle.Text = key is null ? "Plugins" : key == GuidePage ? "Fair play" : SettingsCatalog.All.Single(s => s.Id == key).Label;
        if (IsHandleCreated) ResizeForPage();
    }

    private void RestoreFeatureDefaults()
    {
        if (_selectedPage is null || !_featureSettings.TryGetValue(_selectedPage, out var ids)) return;
        var values = _game.ReadSettings(); var defaults = SettingsCatalog.ReadDefaults(_game.DefaultsPath);
        foreach (var id in ids) values[id] = defaults[id];
        Populate(values); ApplySettings();
    }
}
