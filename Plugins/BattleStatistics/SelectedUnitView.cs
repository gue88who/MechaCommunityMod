using GameRiver.Client;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class SelectedUnitView : IDisposable
{
    private static TableDisplaySettings Settings => DisplaySettings.LiveUnit;
    private readonly ResultsSkin _skin = new();
    private readonly StatsHelpView _help = new();
    private readonly UnitStatsSettingsView _settings = new();
    private readonly SideHudSpace _hud = new();
    private CardInfoPanel? _native;
    private InteractiveUIComponent? _button;
    private Il2CppSystem.Action? _click;
    private bool _open;
    private int _round = -1;
    private float _retryAt, _scroll, _horizontal;
    private int[] _availableRounds = Array.Empty<int>();
    private int _dragControl;
    private Vector2 _dragPoint;
    private Rect _panel;
    private GUIStyle? _label, _small, _title, _action;
    private StatsTableRow[] _rows = Array.Empty<StatsTableRow>();
    private readonly HashSet<(bool Right, string Id)> _expanded = new();
    private readonly HashSet<(bool Right, string Id)> _expandedUnits = new();
    private string? _selectedId;
    private int _session = -1;
    private readonly GameObject _input;
    private readonly RectTransform _shield;

    internal SelectedUnitView()
    {
        _input = new GameObject("MechaCommunityModSelectedUnitInput");
        Object.DontDestroyOnLoad(_input);
        var canvas = _input.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 29999;
        _input.AddComponent<GraphicRaycaster>();
        _shield = new GameObject("InputShield").AddComponent<RectTransform>();
        _shield.SetParent(_input.transform, false);
        _shield.anchorMin = _shield.anchorMax = _shield.pivot = Vector2.zero;
        _shield.gameObject.AddComponent<Image>().color = Color.clear;
        _input.SetActive(false);
    }

    internal void Observe(GameReader reader, bool resultsVisible)
    {
        if (_session != reader.History.Session)
        { _session = reader.History.Session; _open = false; _round = -1; _expanded.Clear(); _expandedUnits.Clear(); _help.Close(); _settings.Close(); }
        if (_native == null || !_native.gameObject.activeInHierarchy)
        {
            if (Time.unscaledTime >= _retryAt)
            {
                _retryAt = Time.unscaledTime + 1;
                var native = Resources.FindObjectsOfTypeAll<CardInfoPanel>()
                    .FirstOrDefault(p => p != null && p.gameObject.activeInHierarchy && p.actor != null);
                if (native != null && (_native == null || native.Pointer != _native.Pointer))
                { DestroyButton(); _native = native; }
            }
        }
        var available = !resultsVisible && !OverlayUiLayer.Obscured() && _native != null && _native.gameObject.activeInHierarchy && _native.actor != null;
        if (!available)
        { if (_button != null) _button.gameObject.SetActive(false); _input.SetActive(false); return; }
        var camera = _native!.uIComponentShoulderCam;
        if (_button == null && camera != null)
        {
            _skin.Load();
            _button = ResultsButton.CreateButton(camera, "MechaCommunityMod_SelectedUnitStats", () => { _open = !_open; _scroll = 0; }, out _click);
            if (_button.TryCast<GameRiver.Client.UI.TooltipButton>() is { } tooltipButton)
                tooltipButton.menuName = "Unit stats";
            foreach (var tooltip in _button.GetComponentsInChildren<CommonTooltipSource>(true))
            {
                tooltip.SetOverrideTooltipData(null);
                tooltip.SetTitle("Unit stats"); tooltip.SetTitleDirect(true);
                tooltip.SetDescription(""); tooltip.SetDescriptionDirect(true);
                tooltip.SetDetail(""); tooltip.SetDetailDirect(true);
                if (tooltip.tooltipController != null)
                    tooltip.tooltipController.source = tooltip.Cast<ITooltipUIElement>();
            }
            var rect = _button.GetComponent<RectTransform>();
            var source = camera.GetComponent<RectTransform>();
            _button.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rect.anchoredPosition = source.anchoredPosition + new Vector2(0, source.rect.height + 6);
            rect.sizeDelta = source.sizeDelta;
            // Keep the camera button's native frame/effects, replace its camera glyph.
            var statsIcon = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && s.name == "Icon_Battle_Statistic");
            foreach (var image in _button.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && !image.sprite.name.StartsWith("button_", StringComparison.Ordinal)
                    && !image.gameObject.name.Contains("background", StringComparison.OrdinalIgnoreCase)
                    && !image.gameObject.name.Contains("frame", StringComparison.OrdinalIgnoreCase))
                { image.sprite = statsIcon; image.preserveAspect = true; }
            foreach (var label in _button.GetComponentsInChildren<GRText>(true)) label.text = "";
            BattleStatisticsPlugin.Logger.LogInfo("Added squad Stats above the selected-unit camera button.");
        }
        if (_button != null) _button.gameObject.SetActive(true);
        if (!_open) { _input.SetActive(false); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) { if (_help.Open) _help.Close(); else if (_settings.Open) _settings.Close(); else Close(); return; }
        var actor = _native.actor!;
        var id = SquadTracker.SelectedId(actor);
        if (_selectedId != id) { _selectedId = id; _round = -1; _scroll = _horizontal = 0; _expanded.Clear(); _expandedUnits.Clear(); }
        _availableRounds = id is null ? Array.Empty<int>() : UnitSelection.AvailableRounds(reader.History, reader.Costs, reader.Boards, id);
        if (_round > 0 && !_availableRounds.Contains(_round)) _round = -1;
        var round = _round < 0 ? _availableRounds.LastOrDefault() : _round;
        var record = round == 0 ? reader.History.Overall() : reader.History.Find(round);
        if (record == null) { _round = -1; _rows = Array.Empty<StatsTableRow>(); }
        else
        {
            var team = actor.fightMech?.GetCurrentTeamController()?.GetTeamIndex()
                ?? actor.GetOwner()?.GetFightTeamController()?.GetTeamIndex();
            var right = team is { } t && reader.IsRightTeam(t);
            var rows = StatsTable.Sort(StatsTable.Rows(record.Left, false, true,
                reader.Costs.Get(round, false), k => reader.Describe(k).Name,
                squadCosts: reader.Costs.GetSquads(round, false), groupChildren: false)
                .Concat(StatsTable.Rows(record.Right, true, true, reader.Costs.Get(round, true), k => reader.Describe(k).Name,
                    squadCosts: reader.Costs.GetSquads(round, true), groupChildren: false)), StatsColumn.Damage, true);
            var selected = id is null ? Array.Empty<StatsTableRow>() : UnitSelection.Rows(rows, id, right);
            // Isolate the selected squad before grouping its children, so selecting
            // a captured unit never includes its same-type siblings' statistics.
            _rows = StatsTable.Sort(StatsTable.GroupAttributed(selected, k => reader.Describe(k).Name, attachedOnly: true),
                StatsColumn.Damage, true);
        }
        if (_rows.Length == 0 && actor.GetParentCard() is { } selectedCard)
        {
            var data = selectedCard.GetUnitData();
            _rows = new[] { new StatsTableRow(id ?? "selected", new(0, data.GetMechID()),
                reader.IsRightTeam(actor.GetOwner()?.GetFightTeamController()?.GetTeamIndex() ?? -1), data.GetName(),
                null, null, null, null, null, null, null, null,
                _availableRounds.Length == 0 ? Math.Max(0, data.GetBaseSupply()) + Math.Max(0L, (long)selectedCard.GetSellSupply() - selectedCard.GetBuySupply())
                    : id is not null && (reader.Costs.GetSquads(round, false).TryGetValue(id, out var investment)
                        || reader.Costs.GetSquads(round, true).TryGetValue(id, out investment)) ? investment.Purchase + investment.Upgrade : null) };
        }
        UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        if (!Position()) { _input.SetActive(false); return; }
        _shield.anchoredPosition = new Vector2(_panel.x, Screen.height - _panel.yMax);
        _shield.sizeDelta = new Vector2(_panel.width, _panel.height);
        _input.SetActive(true);
    }

    private bool Position()
    {
        var rect = _native!.GetComponent<RectTransform>();
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        rect.GetWorldCorners(corners);
        var canvas = _native.GetComponentInParent<Canvas>();
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var points = Enumerable.Range(0, 4).Select(i => RectTransformUtility.WorldToScreenPoint(cam, corners[i])).ToArray();
        var native = new Rect(points.Min(p => p.x), Screen.height - points.Max(p => p.y),
            points.Max(p => p.x) - points.Min(p => p.x), points.Max(p => p.y) - points.Min(p => p.y));
        // Size to visible columns, retaining only enough room for the title/actions.
        var desiredWidth = Math.Max(460, 24 + (Settings.Levels.Value ? 250 : 218)
            + Settings.Columns().Length * 96);
        var height = Math.Min(native.height, Screen.height - 24);
        // Request pixel coordinates, matching this IMGUI popup. Include native
        // controls outside the card's root rectangle (equipment and side HUD).
        var bounds = _hud.ReadBounds(1080f / Screen.height);
        // Some card controls extend beyond the root RectTransform. Start beyond
        // those visible controls too, rather than letting them block the lane.
        var cardRight = bounds.LowerControls.Where(r => r.Bottom > native.yMin && r.Top < native.yMax
            && r.Left < native.xMax && r.Right > native.xMin && r.Right - r.Left < Screen.width * .5f)
            .Select(r => r.Right).DefaultIfEmpty(native.xMax).Max();
        var placement = SideHudLayout.SelectedUnit(bounds.Controls.Concat(bounds.LowerControls),
            new(native.xMin, native.yMin, Math.Max(native.xMax, cardRight), native.yMax), Screen.width, Screen.height, desiredWidth, height);
        if (placement is not { } area) return false;
        _panel = new Rect(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top);
        return true;
    }

    internal void Draw(GameReader reader)
    {
        if (!_open || !_input.activeSelf || _native == null || OverlayUiLayer.Obscured()) return;
        _skin.Load();
        _label ??= new GUIStyle { font = _skin.Body, fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        _small ??= new GUIStyle { font = _skin.Body, fontSize = 14, alignment = TextAnchor.MiddleLeft };
        _title ??= new GUIStyle { font = _skin.Title, fontSize = 16, alignment = TextAnchor.MiddleLeft };
        _action ??= new GUIStyle { font = _skin.Body, fontSize = 14, alignment = TextAnchor.MiddleCenter };
        foreach (var style in new[] { _label, _small, _title, _action })
            style.normal.textColor = style.hover.textColor = style.active.textColor = Color.white;
        var oldDepth = GUI.depth; var oldColor = GUI.color;
        GUI.depth = -9900;
        try
        {
            if (_help.Open) { _help.Draw(_panel, _skin); return; }
            Fill(_panel, new Color(0.12f, 0.16f, 0.22f, 0.6f));
            _skin.Art(_panel, "replayUI_frame_2");
            _skin.Art(new Rect(_panel.x + 10, _panel.y + 8, _panel.width - 196, 40), "button_b_sp1");
            Text(new Rect(_panel.x + 24, _panel.y + 14, _panel.width - 212, 28), "UNIT STATS", _title);
            if (Button(new Rect(_panel.xMax - 44, _panel.y + 12, 30, 28), "X")) { Close(); return; }
            if (Button(new Rect(_panel.xMax - 80, _panel.y + 12, 30, 28), "?")) { _help.Toggle(); _settings.Close(); }
            if (Button(new Rect(_panel.xMax - 174, _panel.y + 12, 88, 28), "Settings")) _settings.Toggle();
            if (_settings.Open)
            {
                _settings.Draw(new Rect(_panel.x + 12, _panel.y + 56, _panel.width - 24, _panel.height - 72), _skin, Settings);
                if (_panel.Contains(Event.current.mousePosition) && Event.current.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) Event.current.Use();
                return;
            }
            var selectedRound = _round < 0 ? _availableRounds.LastOrDefault() : _round;
            var lastRound = Math.Max(reader.History.Rounds.Select(r => r.Number).DefaultIfEmpty().Max(), _availableRounds.LastOrDefault());
            const float roundWidth = 46, roundPitch = 50, roundHeight = 28, roundRowPitch = 32;
            var perLine = Math.Max(1, (int)((_panel.width - 24) / roundPitch));
            var roundLines = Math.Max(1, (lastRound + 1 + perLine - 1) / perLine);
            var enabled = GUI.enabled;
            for (var round = 0; round <= lastRound; round++)
            {
                var available = round == 0 ? _availableRounds.Length > 0 : _availableRounds.Contains(round);
                GUI.enabled = enabled && available;
                var rect = new Rect(_panel.x + 12 + round % perLine * roundPitch,
                    _panel.y + 56 + round / perLine * roundRowPitch, roundWidth, roundHeight);
                var selected = available && (_round == 0 ? round == 0 : round == selectedRound);
                var tint = GUI.color;
                if (!available) GUI.color = new Color(0.42f, 0.42f, 0.42f, 0.65f);
                _skin.Art(rect, selected ? "button_y_x1" : "button_b_x1");
                if (GUI.Button(rect, round == 0 ? "All" : $"R{round}", _action)) { _round = round; _scroll = 0; }
                GUI.color = tint;
            }
            GUI.enabled = enabled;
            var columns = Settings.Columns();
            var visible = StatsTable.Collapse(_rows, _expandedUnits, Settings.Contributions.Value);
            var details = Settings.Sources.Value ? StatsTable.Details(visible, _expanded) : visible;
            var tableTop = 62 + roundLines * roundRowPitch;
            var table = new Rect(_panel.x + 12, _panel.y + tableTop, _panel.width - 24, Math.Max(80, _panel.height - tableTop - 16));
            const float cellWidth = 96, header = 34, rowHeight = 46;
            var frozen = Settings.Levels.Value ? 250f : 218f;
            var sourceSlot = Settings.Sources.Value ? 28 : 0;
            var attributedSlot = Settings.Contributions.Value ? 28 : 0;
            var actionWidth = sourceSlot + attributedSlot;
            string? levelHint = null;
            var numericWidth = table.width - frozen;
            var maxX = Math.Max(0, columns.Length * cellWidth - numericWidth);
            var maxY = Math.Max(0, details.Length * rowHeight - (table.height - header));
            _horizontal = Math.Clamp(_horizontal, 0, maxX); _scroll = Math.Clamp(_scroll, 0, maxY);
            if (Event.current.type == EventType.ScrollWheel && table.Contains(Event.current.mousePosition))
            {
                if (Event.current.shift) _horizontal = Math.Clamp(_horizontal + Event.current.delta.y * 24, 0, maxX);
                else _scroll = Math.Clamp(_scroll + Event.current.delta.y * 24, 0, maxY);
                Event.current.Use();
            }
            _skin.Art(table, "panel_blue_0_with_corners");
            var dragControl = GUIUtility.GetControlID(106, FocusType.Passive);
            Button(new Rect(table.x, table.y, frozen - 2, header - 2), "UNIT");
            GUI.BeginGroup(new Rect(table.x + frozen, table.y, numericWidth, header));
            try { for (var c = 0; c < columns.Length; c++) Button(new Rect(c * cellWidth - _horizontal, 0, cellWidth - 2, header - 2), ResultsView.Title(columns[c])); }
            finally { GUI.EndGroup(); }
            var bodyHeight = table.height - header;
            GUI.BeginGroup(new Rect(table.x, table.y + header, frozen, bodyHeight));
            try
            {
                for (var i = 0; i < details.Length; i++)
                {
                    var row = details[i]; var y = i * rowHeight - _scroll;
                    var accent = row.AccentRight ? new Color(1f, 0.38f, 0.37f) : new Color(0.28f, 0.61f, 1f);
                    Fill(new Rect(0, y, frozen, rowHeight - 1), new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, 0.65f));
                    Fill(new Rect(0, y + 4, 3, rowHeight - 8), accent);
                    var indent = Math.Min(row.Depth, 4) * 8;
                    if (row.Source is { } source) _skin.SourcePortrait(new Rect(6 + indent, y + 8, 28, 28), source);
                    else _skin.Portrait(new Rect(6 + indent, y + 8, 28, 28), reader.Describe(row.Unit));
                    var expandable = Settings.Sources.Value && row.HasDetails;
                    var levelWidth = Settings.Levels.Value && row.HasLevelBadge ? 32 : 0;
                    if (levelWidth > 0 && UnitLevelBadge.From(row.LevelSquads) is { } badge)
                    {
                        var levelRect = new Rect(38 + indent, y + 9, 28, 28);
                        _skin.LevelBadge(levelRect, badge);
                        if (GUI.enabled && levelRect.Contains(Event.current.mousePosition)) levelHint = badge.Hint;
                    }
                    Text(new Rect(40 + indent + levelWidth, y + 3, frozen - 46 - indent - levelWidth - (row.Breakdown ? 0 : actionWidth), 40), row.Name, _label);
                    if (Settings.Contributions.Value && row.HasAttributedUnits && _skin.AttributedUnitsButton(
                        new Rect(frozen - actionWidth, y + 12, 24, 24),
                        _expandedUnits.Contains((row.Right, row.Id)), _action!))
                    { if (!_expandedUnits.Remove((row.Right, row.Id))) _expandedUnits.Add((row.Right, row.Id)); }
                    if (expandable && Button(new Rect(frozen - 28, y + 12, 24, 24), _expanded.Contains((row.Right, row.Id)) ? "-" : "+"))
                    { if (!_expanded.Remove((row.Right, row.Id))) _expanded.Add((row.Right, row.Id)); }
                }
            }
            finally { GUI.EndGroup(); }
            GUI.BeginGroup(new Rect(table.x + frozen, table.y + header, numericWidth, bodyHeight));
            try
            {
                for (var i = 0; i < details.Length; i++)
                {
                    var y = i * rowHeight - _scroll;
                    Fill(new Rect(0, y, numericWidth, rowHeight - 1), i % 2 == 0 ? new Color(0.055f, 0.085f, 0.13f, 0.55f) : new Color(0.08f, 0.12f, 0.18f, 0.55f));
                    for (var c = 0; c < columns.Length; c++)
                    {
                        var value = details[i].Value(columns[c]);
                        var fractional = columns[c] is StatsColumn.XpGained or StatsColumn.XpFed;
                        var color = columns[c] switch {
                            StatsColumn.DamageOverkill => new Color(0.68f, 0.32f, 0.95f),
                            StatsColumn.TankedOverkill => new Color(1f, 0.79f, 0.12f),
                            StatsColumn.HackAmount => new Color(0.35f, 0.87f, 0.61f),
                            StatsColumn.FailedHack => new Color(1f, 0.58f, 0.36f),
                            StatsColumn.XpGained => new Color(0.45f, 0.9f, 0.65f),
                            StatsColumn.XpFed => new Color(0.95f, 0.7f, 0.26f), _ => Color.white };
                        var old = _small.normal.textColor; _small.normal.textColor = color;
                        Text(new Rect(c * cellWidth - _horizontal + 8, y + 10, cellWidth - 16, 26),
                            value is null or 0 ? "-" : value.Value.ToString(fractional ? "N1" : "N0"), _small);
                        _small.normal.textColor = old;
                    }
                }
            }
            finally { GUI.EndGroup(); }
            var body = new Rect(table.x, table.y + header, table.width, bodyHeight);
            if (levelHint is not null && !_help.Open)
                _skin.LevelHint(Event.current.mousePosition, new Rect(0, 0, Screen.width, Screen.height), levelHint);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && body.Contains(Event.current.mousePosition)
                && GUIUtility.hotControl == 0)
            { _dragControl = dragControl; _dragPoint = Event.current.mousePosition; GUIUtility.hotControl = dragControl; Event.current.Use(); }
            if (GUIUtility.hotControl == dragControl && _dragControl == dragControl)
            {
                if (Event.current.rawType == EventType.MouseDrag)
                {
                    var delta = Event.current.mousePosition - _dragPoint; _dragPoint = Event.current.mousePosition;
                    var next = TablePanning.Move(_horizontal, _scroll, delta.x, delta.y, maxX, maxY);
                    _horizontal = next.X; _scroll = next.Y; Event.current.Use();
                }
                else if (Event.current.rawType == EventType.MouseUp)
                { GUIUtility.hotControl = 0; _dragControl = 0; Event.current.Use(); }
            }
            if (_panel.Contains(Event.current.mousePosition) && Event.current.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) Event.current.Use();
        }
        finally { GUI.depth = oldDepth; GUI.color = oldColor; }
    }
    internal void RefreshVisibility(bool resultsVisible)
    {
        var available = MatchSupport.Allowed && !resultsVisible && _native != null && _native.gameObject.activeInHierarchy
            && _native.actor != null && !OverlayUiLayer.Obscured();
        if (_button != null) _button.gameObject.SetActive(available);
        // Never re-enable an unplaced popup: its default rectangle is at (0,0).
        // Reposition after native HUD transitions, before the next OnGUI pass.
        if (available && _open) UpdatePlacement();
        else _input.SetActive(false);
    }
    private bool Button(Rect rect, string text) { _skin.Art(rect, "button_b_" + (rect.width < 70 ? "x1" : "m1")); return GUI.Button(rect, text, _action ?? GUI.skin.button); }
    private static void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
    private static void Text(Rect rect, string text, GUIStyle style) { var old = GUI.color; GUI.color = Color.white; GUI.Label(rect, text, style); GUI.color = old; }
    internal void Close()
    {
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragControl = 0; _open = false; _settings.Close(); _input.SetActive(false);
    }
    private void DestroyButton() { if (_button != null) Object.Destroy(_button.gameObject); _button = null; _click = null; }
    public void Dispose() { _settings.Close(); DestroyButton(); _skin.Dispose(); Object.Destroy(_input); }
}
