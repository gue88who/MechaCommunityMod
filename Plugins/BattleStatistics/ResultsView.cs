using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class ResultsView : IDisposable
{
    private static TableDisplaySettings Settings => DisplaySettings.PostGame;
    private const float BaseWidth = 1180, Height = 760;
    private float Width = BaseWidth;
    private static float FrozenWidth => Settings.Levels.Value ? 332 : 300;
    private static readonly Color Blue = new(0.28f, 0.61f, 1f);
    private static readonly Color Red = new(1f, 0.38f, 0.37f);
    private static readonly Color Yellow = new(1f, 0.79f, 0.12f);
    private static readonly Color Purple = new(0.68f, 0.32f, 0.95f);
    private static readonly Color Muted = new(0.65f, 0.71f, 0.8f);
    private static readonly Color Surface = new(0.085f, 0.11f, 0.16f, 1);
    private static readonly Color Gold = new(0.95f, 0.7f, 0.26f);
    private readonly GameObject _blocker;
    private readonly RectTransform _shield;
    private readonly ResultsButton _launcher;
    private readonly ResultsSkin _skin = new();
    private readonly StatsHelpView _help = new();
    private GUIStyle? _label, _small, _heading, _button, _number, _value, _caption, _unit;
    private Vector2 _roundScroll, _tableScroll;
    private int _session = -1, _selected, _cachedRevision = -1;
    private bool _available, _settingsOpen, _unitFilterOpen, _descending = true;
    private readonly HashSet<UnitKey> _hiddenTypes = new();
    private float _unitFilterScroll;
    private bool _showLeft = true, _showRight = true;
    private Vector2 _dragPoint;
    private int _dragControl;
    private readonly HashSet<(bool Right, string Id)> _expandedSources = new();
    private readonly HashSet<(bool Right, string Id)> _expandedUnits = new();
    private StatsTableRow? _pinnedMap;
    private (bool Right, string Id)? _mapUnit;
    private int _mapRound = -1;
    private Rect _mapPanel;
    private readonly BoardMapInput _mapInput = new();
    private BoardMapButton[] _mapButtons = Array.Empty<BoardMapButton>();
    private bool _tablePointerBlocked;
    private Vector2 _tableMousePosition;
    private (Rect Anchor, string Text)? _eyeHint;
    private string? _levelHint;
    private readonly Dictionary<BoardPiece, BoardEdge[]> _boardEdges = new();
    private StatsColumn? _sortColumn = StatsColumn.Damage;
    private RoundRecord? _overall;
    internal bool Visible { get; private set; }

    internal ResultsView()
    {
        // Consume native UI raycasts while the results screen covers the game's result window.
        _blocker = new GameObject("MechaCommunityModResultsInput");
        Object.DontDestroyOnLoad(_blocker);
        var canvas = _blocker.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        _blocker.AddComponent<GraphicRaycaster>();
        _shield = new GameObject("InputShield").AddComponent<RectTransform>();
        _shield.SetParent(_blocker.transform, false);
        _shield.anchorMin = Vector2.zero;
        _shield.anchorMax = Vector2.one;
        _shield.offsetMin = _shield.offsetMax = Vector2.zero;
        _shield.gameObject.AddComponent<Image>().color = Color.clear;
        _launcher = new ResultsButton(() => SetVisible(true));
        _blocker.SetActive(false);
    }

    internal void Observe(MatchHistory history)
    {
        _available = history.Ended && history.Count > 0;
        if (_session != history.Session)
        {
            _session = history.Session;
            _selected = 0;
            _cachedRevision = -1;

            _settingsOpen = false;
            _unitFilterOpen = false; _hiddenTypes.Clear(); _unitFilterScroll = 0;
            _help.Close();
            _showLeft = _showRight = true;
            _expandedSources.Clear();
            _expandedUnits.Clear();
            _pinnedMap = null;
            _mapUnit = null; _mapRound = -1;
            _boardEdges.Clear();
            _skin.ClearAvatars();
            _roundScroll = _tableScroll = Vector2.zero;
            SetVisible(false);
        }
        if (!history.Ended)
        {
            // A replay rewind/retry resumes play rather than retaining the old result overlay.
            SetVisible(false);
        }
        UpdateInput();
    }

    internal void Toggle(MatchHistory history)
    {
        if (history.Ended && history.Count > 0) SetVisible(!Visible);
    }
    internal void Dismiss()
    {
        if (_help.Open) _help.Close();
        else if (_unitFilterOpen) _unitFilterOpen = false;
        else if (_settingsOpen) _settingsOpen = false;
        else if (_pinnedMap is not null) _pinnedMap = null;
        else SetVisible(false);
    }

    internal void WindowShown(GameRiver.Client.GRWindow window, MatchHistory history)
    {
        Observe(history);
        _launcher.WindowShown(window, _available && !Visible);
    }

    private void SetVisible(bool visible)
    {
        if (!visible && _dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        if (!visible) _mapInput.Cancel();
        Visible = visible;
        UpdateInput();
    }

    internal void Close() => SetVisible(false);

    private void UpdateInput()
    {
        _blocker.SetActive(_available && Visible);
        _shield.gameObject.SetActive(Visible);
        _launcher.Refresh(_available && !Visible);
    }

    internal void Draw(MatchHistory history, GameReader reader)
    {
        if (!Visible) return;
        using var timing = new SlowOperation("report rendering");
        InitStyles();
        if (_cachedRevision != history.Revision)
        { _cachedRevision = history.Revision; _overall = history.Overall(); }
        if (_selected != 0 && history.Find(_selected) is null) _selected = 0;
        var selected = history.Find(_selected) ?? _overall;
        if (selected is null) return;
        _eyeHint = null;
        _levelHint = null;
        var oldMatrix = GUI.matrix; var oldColor = GUI.color; var oldDepth = GUI.depth; var oldEnabled = GUI.enabled;
        // Keep the existing UI scale; use spare horizontal space for columns.
        var scale = Math.Min(Screen.width / (BaseWidth + 40), Screen.height / (Height + 40));
        if (scale <= 0) return;
        try
        {
            GUI.depth = -10000;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            var screen = new Rect(0, 0, Screen.width / scale, Screen.height / scale);
            // The header and player controls need 940 units; the table determines
            // any additional width. BaseWidth controls scale, not minimum size.
            Width = Math.Min(screen.width - 40, Math.Max(940,
                194 + FrozenWidth + 14 + Columns().Length * 116));
            Fill(screen, new Color(0, 0, 0, 0.82f));
            var panel = new Rect((screen.width - Width) / 2, (screen.height - Height) / 2, Width, Height);
            GUI.BeginGroup(panel);
            try
            {
                Fill(new Rect(0, 0, Width, Height), new Color(0.035f, 0.047f, 0.075f, 1));
                if (!_skin.Art(new Rect(0, 0, Width, Height), "replayUI_frame_2"))
                    Frame(new Rect(0, 0, Width, Height), Muted);
                _skin.Art(new Rect(14, 12, Width - 560, 56), "button_b_sp1");
                Text(new Rect(36, 24, Width - 610, 30), "BATTLE STATS", Color.white, _heading);
                if (history.Outcome is { } outcome) Text(new Rect(Width - 530, 28, 220, 23), outcome, Muted, _small);
                if (Button(new Rect(Width - 302, 18, 34, 36), "?", _help.Open)) { _help.Toggle(); _settingsOpen = false; _unitFilterOpen = false; }
                GUI.enabled = oldEnabled && !_help.Open;
                if (Button(new Rect(Width - 130, 18, 108, 36), "CLOSE")) SetVisible(false);
                var settings = new Rect(Width - 258, 18, 116, 36);
                var dropdown = new Rect(Width - 368, 58, 346, 696);
                if (_settingsOpen && Event.current.type == EventType.MouseDown
                    && !dropdown.Contains(Event.current.mousePosition) && !settings.Contains(Event.current.mousePosition))
                { _settingsOpen = false; Event.current.Use(); }
                if (Button(settings, "SETTINGS", _settingsOpen)) { _settingsOpen = !_settingsOpen; _unitFilterOpen = false; }
                var filterPanel = new Rect(172, 186, 300, 480);
                if (_unitFilterOpen && Event.current.type == EventType.MouseDown
                    && !filterPanel.Contains(Event.current.mousePosition) && !new Rect(172, 146, FrozenWidth, 38).Contains(Event.current.mousePosition))
                { _unitFilterOpen = false; Event.current.Use(); }
                GUI.enabled = oldEnabled && !_settingsOpen && !_help.Open;
                DrawRounds(history);
                Text(new Rect(176, 78, Width - 686, 28), _selected == 0 ? "ALL ROUNDS" : "ROUND " + _selected, Color.white, _heading);
                Text(new Rect(176, 111, Width - 210, 23), Width < 1140
                    ? "Click headers to sort. Drag to scroll. Totals include child rows."
                    : "Click headers to sort. Drag the table to scroll. Child rows are included in their parent's totals.", Muted, _small);
                PlayerToggle(new Rect(Width - 494, 76, 222, 34), history.LeftName, reader.LeftPortrait, reader.Avatars.Ready(reader.LeftPortrait), Blue, ref _showLeft);
                PlayerToggle(new Rect(Width - 256, 76, 222, 34), history.RightName, reader.RightPortrait, reader.Avatars.Ready(reader.RightPortrait), Red, ref _showRight);
                var rows = StatsTable.Rows(selected.Left, false, DisplaySettings.TableIndividuals.Value,
                    reader.Costs.Get(_selected, false), k => reader.Describe(k).Name, NamedUnit, reader.Costs.GetSquads(_selected, false))
                    .Concat(StatsTable.Rows(selected.Right, true, DisplaySettings.TableIndividuals.Value,
                        reader.Costs.Get(_selected, true), k => reader.Describe(k).Name, NamedUnit, reader.Costs.GetSquads(_selected, true)));
                var columns = Columns();
                if (_sortColumn is { } sort && !columns.Contains(sort))
                { _sortColumn = null; _descending = false; }
                var ordered = StatsTable.Sort(rows.Where(r => r.Right ? _showRight : _showLeft),
                    _sortColumn, _descending);
                GUI.enabled = oldEnabled && !_settingsOpen && !_help.Open;
                GUI.enabled &= !_unitFilterOpen || new Rect(172, 146, FrozenWidth, 38).Contains(Event.current.mousePosition);
                var visibleRows = StatsTable.Collapse(ordered.Where(r => StatsTable.Useful(r) && !_hiddenTypes.Contains(r.Unit)), _expandedUnits,
                    Settings.Contributions.Value);
                // Mask the pointer, rather than disabling the table: disabled GUI
                // controls dim their labels and still trigger custom hover artwork.
                // Keep control IDs stable and restore the real pointer for the map.
                _tableMousePosition = Event.current.mousePosition;
                _tablePointerBlocked = _pinnedMap is not null && !_settingsOpen && !_help.Open && !_unitFilterOpen
                    && _mapPanel.Contains(_tableMousePosition);
                HandleMapInput();
                try
                {
                    if (_tablePointerBlocked) Event.current.mousePosition = new Vector2(-100000, -100000);
                    DrawTable(new Rect(172, 146, Width - 194, 570), Settings.Sources.Value
                        ? StatsTable.Details(visibleRows, _expandedSources) : visibleRows, columns, reader);
                }
                finally
                {
                    Event.current.mousePosition = _tableMousePosition;
                    _tablePointerBlocked = false;
                }
                GUI.enabled = oldEnabled;
                if (!_help.Open && !_settingsOpen && !_unitFilterOpen && _pinnedMap is { } mapRow) DrawBoard(reader.Boards, mapRow);
                if (!_help.Open && !_settingsOpen && !_unitFilterOpen && _eyeHint is { } hint) DrawEyeHint(hint.Anchor, hint.Text);
                if (!_help.Open && !_settingsOpen && !_unitFilterOpen && _pinnedMap is null && _levelHint is { } levelHint)
                    _skin.LevelHint(Event.current.mousePosition, new Rect(0, 0, Width, Height), levelHint);
                if (_unitFilterOpen) DrawUnitFilter(filterPanel, history, reader);
                if (_settingsOpen) DrawSettings(dropdown);
                if (_help.Open) _help.Draw(new Rect((Width - 720) / 2, 76, 720, 630), _skin);
            }
            finally { GUI.EndGroup(); }
            if (Event.current.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) Event.current.Use();
        }
        finally { GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.depth = oldDepth; GUI.enabled = oldEnabled; }
    }

    private void DrawRounds(MatchHistory history)
    {
        var panel = new Rect(16, 78, 140, 638);
        Fill(panel, Surface); _skin.Art(panel, "panel_blue_30_with_corners");
        Text(new Rect(30, 87, 110, 22), "ROUNDS", Muted, _small);
        if (Button(new Rect(24, 118, 124, 34), "All", _selected == 0)) Select(0);
        var viewport = new Rect(24, 165, 124, 535);
        var maximum = Math.Max(0, history.Rounds.Count * 40 - viewport.height);
        Wheel(viewport, ref _roundScroll, 0, maximum);
        Scrollbar(new Rect(viewport.xMax - 8, viewport.y, 8, viewport.height), ref _roundScroll.y,
            viewport.height, history.Rounds.Count * 40, false, 101);
        GUI.BeginGroup(new Rect(viewport.x, viewport.y, viewport.width - 12, viewport.height));
        try
        {
            for (var i = 0; i < history.Rounds.Count; i++)
            {
                var round = history.Rounds[i]; var y = i * 40 - _roundScroll.y;
                if (y + 34 < 0 || y > viewport.height) continue;
                if (Button(new Rect(0, y, 108, 34), "Round " + round.Number + (round.Complete ? "" : " *"), _selected == round.Number)) Select(round.Number);
            }
        }
        finally { GUI.EndGroup(); }
    }
    private void Select(int round) { _selected = round; _tableScroll.y = 0; _pinnedMap = null; }
    private static bool NamedUnit(UnitKey key) => key.Kind != (int)GameRiver.Fight.DamageRecorderType.Construction;
    private void PlayerToggle(Rect rect, string name, string portrait, SavedAvatar? avatar, Color accent, ref bool visible)
    {
        if (Button(rect, "", visible)) { visible = !visible; _tableScroll.y = 0; }
        Fill(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4),
            new Color(accent.r, accent.g, accent.b, visible ? 0.25f : 0.06f));
        _skin.PlayerPortrait(new Rect(rect.x + 5, rect.y + 3, 28, 28), portrait, avatar);
        Text(new Rect(rect.x + 42, rect.y + 4, rect.width - 50, 26), name, visible ? accent : Muted, _small);
    }

    internal static StatsColumn[] Columns() => Settings.Columns();
    internal static string Title(StatsColumn column) => column switch {
        StatsColumn.DamageOverkill => "Dmg overkill", StatsColumn.TankedOverkill => "Tank overkill",
        StatsColumn.XpGained => "XP gained", StatsColumn.XpFed => "XP fed", StatsColumn.CoreDamage => "Core dmg",
        StatsColumn.HackAmount => "Hack amount", StatsColumn.FailedHack => "Failed hack", _ => column.ToString() };
    private void Sort(StatsColumn? column)
    {
        if (_sortColumn == column) _descending = !_descending;
        else { _sortColumn = column; _descending = column is not null; }
        _tableScroll.y = 0;
    }
    private string Arrow(bool selected) => selected ? (_descending ? " v" : " ^") : "";

    private void DrawUnitFilter(Rect panel, MatchHistory history, GameReader reader)
    {
        Fill(panel, new Color(0.035f, 0.047f, 0.075f, 1));
        _skin.Art(panel, "panel_blue_0_with_corners");
        if (Button(new Rect(panel.x + 10, panel.y + 10, panel.width - 20, 30), "RESET FILTER"))
        { _hiddenTypes.Clear(); _tableScroll.y = 0; }
        var all = history.Overall();
        var types = all.Left.Rows.Concat(all.Right.Rows).Select(r => r.Unit)
            .Concat(reader.Costs.Get(0, false)?.Keys ?? Enumerable.Empty<UnitKey>())
            .Concat(reader.Costs.Get(0, true)?.Keys ?? Enumerable.Empty<UnitKey>())
            .Distinct().OrderBy(k => reader.Describe(k).Name).ToArray();
        var viewport = new Rect(panel.x + 10, panel.y + 48, panel.width - 20, panel.height - 58);
        var max = Math.Max(0, types.Length * 38 - viewport.height);
        if (Event.current.type == EventType.ScrollWheel && panel.Contains(Event.current.mousePosition))
        { _unitFilterScroll = Math.Clamp(_unitFilterScroll + Event.current.delta.y * 28, 0, max); Event.current.Use(); }
        _unitFilterScroll = Math.Clamp(_unitFilterScroll, 0, max);
        GUI.BeginGroup(viewport);
        try
        {
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i]; var y = i * 38 - _unitFilterScroll;
                var show = !_hiddenTypes.Contains(type);
                var changed = false;
                if (Button(new Rect(0, y, viewport.width - 40, 34), reader.Describe(type).Name, show))
                {
                    _hiddenTypes.Clear();
                    _hiddenTypes.UnionWith(types.Where(other => other != type));
                    changed = true;
                }
                if (Button(new Rect(viewport.width - 34, y, 34, 34), show ? "ON" : "OFF", show))
                {
                    if (show) _hiddenTypes.Add(type); else _hiddenTypes.Remove(type);
                    changed = true;
                }
                if (changed) { _tableScroll.y = 0; _pinnedMap = null; }
            }
        }
        finally { GUI.EndGroup(); }
    }
    private void DrawTable(Rect rect, StatsTableRow[] rows, StatsColumn[] columns, GameReader reader)
    {
        const float cellWidth = 116, header = 38, rowHeight = 48;
        var frozen = FrozenWidth;
        var sourceSlot = Settings.Sources.Value ? 28 : 0;
        var attributedSlot = Settings.Contributions.Value ? 28 : 0;
        var actionWidth = 28 + sourceSlot + attributedSlot;
        Fill(rect, Surface); _skin.Art(rect, "panel_blue_0_with_corners");
        var bodyHeight = rect.height - header - 18;
        var numericWidth = rect.width - frozen - 14;
        var contentWidth = columns.Length * cellWidth;
        var contentHeight = rows.Length * rowHeight;
        var body = new Rect(rect.x, rect.y + header, rect.width - 14, bodyHeight);
        Wheel(body, ref _tableScroll, Math.Max(0, contentWidth - numericWidth), Math.Max(0, contentHeight - bodyHeight));
        Scrollbar(new Rect(rect.x + frozen, rect.yMax - 11, numericWidth, 9), ref _tableScroll.x, numericWidth, contentWidth, true, 102,
            _tablePointerBlocked ? _tableMousePosition : null);
        Scrollbar(new Rect(rect.xMax - 10, body.y, 8, bodyHeight), ref _tableScroll.y, bodyHeight, contentHeight, false, 103,
            _tablePointerBlocked ? _tableMousePosition : null);
        // Reserve before virtualized row buttons so dragging retains the same control ID while scrolling.
        var bodyControl = GUIUtility.GetControlID(104, FocusType.Passive);
        var first = Math.Max(0, (int)(_tableScroll.y / rowHeight));
        var last = Math.Min(rows.Length, first + (int)Math.Ceiling(bodyHeight / rowHeight) + 1);
        // Keep the native button styling, without enabling nickname sorting.
        if (Button(new Rect(rect.x, rect.y, frozen, header - 2), _hiddenTypes.Count == 0 ? "FILTER" : $"FILTER ({_hiddenTypes.Count})", _unitFilterOpen))
            _unitFilterOpen = !_unitFilterOpen;
        GUI.BeginGroup(new Rect(rect.x + frozen, rect.y, numericWidth, header));
        try
        {
            for (var c = 0; c < columns.Length; c++)
                if (Button(new Rect(c * cellWidth - _tableScroll.x, 0, cellWidth - 2, header - 2), Title(columns[c]) + Arrow(_sortColumn == columns[c]))) Sort(columns[c]);
        }
        finally { GUI.EndGroup(); }
        GUI.BeginGroup(new Rect(rect.x, body.y, frozen, bodyHeight));
        try
        {
            for (var i = first; i < last; i++)
            {
                var row = rows[i]; var y = i * rowHeight - _tableScroll.y; var accent = row.AccentRight ? Red : Blue;
                Fill(new Rect(0, y, frozen, rowHeight - 1), RowColor(i));
                Fill(new Rect(0, y, frozen, rowHeight - 1), new Color(accent.r, accent.g, accent.b, row.Depth > 0 ? 0.12f : 0.22f));
                Fill(new Rect(0, y + 4, 3, rowHeight - 8), accent);
                var indent = Math.Min(row.Depth, 5) * 18;
                if (row.Depth > 0) DrawConnector(new Rect(indent - 8, y, 16, rowHeight), Muted);
                if (row.Source is { } source) _skin.SourcePortrait(new Rect(8 + indent, y + 6, 34, 34), source);
                else if (row.Unit.Kind is >= 100 and <= 103)
                    _skin.SourcePortrait(new Rect(8 + indent, y + 6, 34, 34),
                        new DamageSource((DamageCategory)(row.Unit.Kind - 100), row.Unit.Id, row.Name));
                else _skin.Portrait(new Rect(8 + indent, y + 6, 34, 34), reader.Describe(row.Unit));
                var hasDetails = Settings.Sources.Value && row.HasDetails;
                var hasPosition = !row.Breakdown && reader.Boards.Find(row, _selected) is not null;
                if (hasPosition)
                {
                    var positionIcon = new Rect(frozen - 28, y + 12, 24, 24);
                    var hover = GUI.enabled && positionIcon.Contains(Event.current.mousePosition);
                    var pinned = _pinnedMap?.Id == row.Id && _pinnedMap.Right == row.Right;
                    if (Button(positionIcon, "", pinned))
                        _pinnedMap = pinned ? null : row;
                    _skin.Art(new Rect(positionIcon.x + 4, positionIcon.y + 4, 16, 16), "icon_eye");
                    if (hover)
                        _eyeHint = (new Rect(rect.x + positionIcon.x, body.y + positionIcon.y, 24, 24),
                            pinned ? "Click to hide unit position" : "Click to show unit position");
                }
                var levelWidth = Settings.Levels.Value && row.HasLevelBadge ? 32 : 0;
                if (levelWidth > 0 && UnitLevelBadge.From(row.LevelSquads) is { } badge)
                {
                    var levelRect = new Rect(46 + indent, y + 10, 28, 28);
                    _skin.LevelBadge(levelRect, badge);
                    if (GUI.enabled && levelRect.Contains(Event.current.mousePosition)) _levelHint = badge.Hint;
                }
                Text(new Rect(50 + indent + levelWidth, y + 3, frozen - 58 - indent - levelWidth - (row.Breakdown ? 0 : actionWidth), 42), row.Name, Color.white, _unit);
                if (Settings.Contributions.Value && row.HasAttributedUnits)
                {
                    var attributed = new Rect(frozen - actionWidth, y + 12, 24, 24);
                    var expanded = _expandedUnits.Contains((row.Right, row.Id));
                    if (_skin.AttributedUnitsButton(attributed, expanded, _button!))
                    { if (!_expandedUnits.Remove((row.Right, row.Id))) _expandedUnits.Add((row.Right, row.Id)); }
                    if (GUI.enabled && attributed.Contains(Event.current.mousePosition))
                        _eyeHint = (new Rect(rect.x + attributed.x, body.y + attributed.y, 24, 24),
                            expanded ? "Hide attributed units" : "Show attributed units");
                }
                if (hasDetails && Button(new Rect(frozen - 28 - sourceSlot, y + 12, 24, 24), _expandedSources.Contains((row.Right, row.Id)) ? "-" : "+"))
                { if (!_expandedSources.Remove((row.Right, row.Id))) _expandedSources.Add((row.Right, row.Id)); }
            }
        }
        finally { GUI.EndGroup(); }
        GUI.BeginGroup(new Rect(rect.x + frozen, body.y, numericWidth, bodyHeight));
        try
        {
            for (var i = first; i < last; i++)
            {
                var y = i * rowHeight - _tableScroll.y;
                Fill(new Rect(0, y, numericWidth, rowHeight - 1), RowColor(i));
                for (var c = 0; c < columns.Length; c++)
                {
                    var value = rows[i].Value(columns[c]);
                    var fractional = columns[c] is StatsColumn.XpGained or StatsColumn.XpFed;
                    var color = columns[c] switch { StatsColumn.DamageOverkill => Purple, StatsColumn.TankedOverkill => Yellow,
                        StatsColumn.HackAmount => new Color(0.35f, 0.87f, 0.61f), StatsColumn.FailedHack => new Color(1f, 0.58f, 0.36f),
                        StatsColumn.XpGained => new Color(0.45f, 0.9f, 0.65f), StatsColumn.XpFed => Gold, _ => Color.white };
                    Text(new Rect(c * cellWidth - _tableScroll.x + 6, y + 10, cellWidth - 18, 28),
                        value is null or 0 ? "-" : value.Value.ToString(fractional ? "N1" : "N0", CultureInfo.InvariantCulture), color, _number);
                }
            }
        }
        finally { GUI.EndGroup(); }
        DragBody(body, Math.Max(0, contentWidth - numericWidth), Math.Max(0, contentHeight - bodyHeight), bodyControl);
        Fill(new Rect(rect.x + frozen - 1, rect.y, 1, rect.height), Muted);
        if (rows.Length == 0) Text(new Rect(rect.x + 20, body.y + 20, rect.width - 40, 28),
            !_showLeft && !_showRight ? "Select a player above" : "No statistics recorded for this round", Muted, _label);
    }
    private static Color RowColor(int index) => index % 2 == 0 ? new Color(0.055f, 0.085f, 0.13f) : new Color(0.08f, 0.12f, 0.18f);
    private void DrawEyeHint(Rect anchor, string text)
    {
        const float width = 216;
        var hint = new Rect(Math.Max(16, anchor.xMax - width), Math.Min(Height - 38, anchor.yMax + 5), width, 30);
        Fill(hint, Surface);
        _skin.Art(hint, "panel_blue_0_with_corners");
        Text(new Rect(hint.x + 12, hint.y + 5, width - 24, 20), text, Muted, _small);
    }
    private void DrawBoard(BoardHistory history, StatsTableRow row)
    {
        var identity = (row.Right, row.Id);
        if (_mapUnit != identity) { _mapUnit = identity; _mapRound = -1; }
        var board = _mapRound > 0 ? history.Get(_mapRound) : history.Find(row, _mapRound == 0 ? 0 : _selected);
        var rounds = history.All();
        var lastRound = rounds.Select(b => b.Round).DefaultIfEmpty().Max();
        var available = rounds.Where(b => b.Select(row).Length > 0).Select(b => b.Round).ToHashSet();
        var bounds = board?.Bounds() ?? new BoardRect(0, 0, 0, 0);
        var pixel = 1f / Math.Max(0.01f, GUI.matrix.m00);
        // Keep one native board cell per physical screen pixel, without rescaling
        // to the popup size. Off-board space is dark; flank regions remain visible.
        var mapLeft = 172 + FrozenWidth;
        var width = Math.Max(250, Math.Min(Width - mapLeft - 22, bounds.Width * pixel + 32));
        var perLine = Math.Max(1, (int)((width - 32) / 44));
        var lines = Math.Max(1, (lastRound + 1 + perLine - 1) / perLine);
        var controlsHeight = lines * 30;
        var height = Math.Max(130, Math.Min(610, bounds.Height * pixel + 76 + controlsHeight));
        var panel = _mapPanel = new Rect(mapLeft, Height - height - 36, width, height);
        Fill(panel, new Color(0.025f, 0.035f, 0.055f, 1));
        _skin.Art(panel, "panel_blue_30_with_corners");
        Text(new Rect(panel.x + 12, panel.y + 8, width - 52, 24),
            $"ROUND {board?.Round}  /  {row.Name}", row.Right ? Red : Blue, _small);
        var buttons = new List<BoardMapButton> { new(-1, panel.xMax - 32, panel.y + 8, 24, 24) };
        for (var round = 0; round <= lastRound; round++)
        {
            buttons.Add(new(round, panel.x + 16 + round % perLine * 44, panel.y + 38 + round / perLine * 30, 40, 26,
                round == 0 ? available.Count > 0 : available.Contains(round)));
        }
        _mapButtons = buttons.ToArray();
        foreach (var button in _mapButtons)
            DrawMapButton(button, button.Action == -1 ? "X" : button.Action == 0 ? "All" : $"R{button.Action}",
                button.Action >= 0 && (_mapRound == 0 ? button.Action == 0 : button.Action == board?.Round));
        if (board is null || bounds.Width == 0 || bounds.Height == 0)
        { Text(new Rect(panel.x + 12, panel.y + 44, width - 24, 30), "No board snapshot recorded", Muted, _small); return; }
        var viewport = new Rect(panel.x + 16, panel.y + 40 + controlsHeight, width - 32, height - 60 - controlsHeight);
        GUI.BeginGroup(viewport);
        try
        {
            var originX = Math.Max(0, (viewport.width - bounds.Width * pixel) / 2);
            var originY = Math.Max(0, (viewport.height - bounds.Height * pixel) / 2);
            var flip = board.FlipVertically();
            void Cells(BoardRect r, Color color)
            {
                var display = BoardSnapshot.DisplayRect(r, bounds, flip);
                Fill(new Rect(originX + display.X * pixel, originY + display.Y * pixel,
                    display.Width * pixel, display.Height * pixel), color);
            }
            foreach (var region in board.Regions)
            {
                Cells(region, new Color(0.12f, 0.16f, 0.22f));
                var border = new Color(0.24f, 0.30f, 0.40f);
                Cells(new(region.X, region.Y, region.Width, 1), border);
                Cells(new(region.X, region.Y + region.Height - 1, region.Width, 1), border);
                Cells(new(region.X, region.Y, 1, region.Height), border);
                Cells(new(region.X + region.Width - 1, region.Y, 1, region.Height), border);
            }
            foreach (var piece in board.Pieces)
                foreach (var cell in piece.Cells) Cells(cell, piece.Right ? Red : Blue);
            var selected = board.Select(row);
            var blink = (int)(Time.unscaledTime * 2) % 2 == 0;
            // Diagonal hatching identifies static objects without covering their
            // player color or drawing outside the recorded occupied cells.
            foreach (var piece in board.Pieces.Where(p => p.Tower || p.Unit.Kind == 1))
                foreach (var cell in piece.Cells)
                    for (var y = cell.Y; y < cell.Y + cell.Height; y++)
                        for (var x = cell.X; x < cell.X + cell.Width; x++)
                            if (((x + y) % 4 + 4) % 4 == 0)
                                Cells(new(x, y, 1, 1), new Color(1, 1, 1, 0.65f));
            // Outline the union of each squad's cells, never its internal grid.
            // Subpixel strokes disappear in this player's GUI rasterizer.
            // A full screen pixel stays inside the footprint; team-tinted dark
            // edges keep even a one-cell footprint distinguishable from terrain.
            var thickness = pixel;
            foreach (var piece in board.Pieces)
            {
                if (!_boardEdges.TryGetValue(piece, out var edges)) _boardEdges[piece] = edges = BoardOutline.Edges(piece);
                foreach (var edge in edges)
                {
                    var display = BoardSnapshot.DisplayRect(new(edge.X, edge.Y, 1, 1), bounds, flip);
                    var x = originX + display.X * pixel;
                    var y = originY + display.Y * pixel;
                    var side = flip ? (edge.Side < 2 ? 1 - edge.Side : 5 - edge.Side) : edge.Side;
                    var stroke = side switch {
                        0 => new Rect(x, y, thickness, pixel),
                        1 => new Rect(x + pixel - thickness, y, thickness, pixel),
                        2 => new Rect(x, y + pixel - thickness, pixel, thickness),
                        _ => new Rect(x, y, pixel, thickness) };
                    var borderColor = piece.Right ? new Color(0.38f, 0.08f, 0.08f) : new Color(0.06f, 0.19f, 0.38f);
                    Fill(stroke, borderColor);
                }
            }
            // Draw the highlight after hatching and outlines: a one-pixel unit's
            // entire footprint otherwise gets repainted by its own border.
            if (blink)
                foreach (var piece in selected)
                    foreach (var cell in piece.Cells) Cells(cell, Color.white);
        }
        finally { GUI.EndGroup(); }
        if (board.Select(row).Length == 0)
            Text(new Rect(panel.x + 12, panel.yMax - 23, width - 24, 20), "No starting position recorded", Muted, _small);
    }
    private void HandleMapInput()
    {
        var e = Event.current;
        if (_pinnedMap is null || _settingsOpen || _help.Open || _unitFilterOpen)
        { _mapInput.Cancel(); return; }
        // The popup owns these pointer events before any table control runs.
        // Its actions use stable logical IDs, independent of virtualized rows,
        // sprite clipping groups and native GUI.Button control numbering.
        if (GUIUtility.hotControl != 0 || (!_tablePointerBlocked && !_mapInput.Captured)) return;
        if (e.type == EventType.MouseDown && e.button == 0)
            _mapInput.Press(_mapButtons, _tableMousePosition.x, _tableMousePosition.y);
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            var action = _mapInput.Release(_mapButtons, _tableMousePosition.x, _tableMousePosition.y);
            if (action == -1) _pinnedMap = null;
            else if (action is >= 0) _mapRound = action.Value;
        }
        if (e.type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag or EventType.ScrollWheel) e.Use();
    }
    private void DrawMapButton(BoardMapButton button, string text, bool selected)
    {
        var rect = new Rect((float)button.X, (float)button.Y, (float)button.Width, (float)button.Height);
        var enabled = GUI.enabled && button.Enabled;
        var hover = enabled && rect.Contains(Event.current.mousePosition);
        var tint = enabled ? Color.white : new Color(0.42f, 0.42f, 0.42f, 0.65f);
        if (!_skin.Art(rect, "button_" + (selected ? "y" : "b") + "_x" + (hover ? "2" : "1"), tint))
        { Fill(rect, selected ? new Color(0.34f, 0.28f, 0.15f) : Surface); Frame(rect, Muted); }
        Text(rect, text, enabled ? Color.white : new Color(0.35f, 0.4f, 0.48f), _button);
    }
    private static void DrawConnector(Rect rect, Color color)
    {
        Fill(new Rect(rect.x + 2, rect.y, 1, rect.height / 2), color);
        Fill(new Rect(rect.x + 2, rect.y + rect.height / 2, 9, 1), color);
    }

    private void DragBody(Rect body, float maxX, float maxY, int id)
    {
        var e = Event.current;
        if (GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && body.Contains(e.mousePosition) && GUIUtility.hotControl == 0)
        { _dragControl = id; _dragPoint = e.mousePosition; GUIUtility.hotControl = id; e.Use(); }
        if (GUIUtility.hotControl != id) return;
        if (e.rawType == EventType.MouseDrag)
        {
            var pointer = _tablePointerBlocked ? _tableMousePosition : e.mousePosition;
            var delta = pointer - _dragPoint; _dragPoint = pointer;
            var next = TablePanning.Move(_tableScroll.x, _tableScroll.y, delta.x, delta.y, maxX, maxY);
            _tableScroll = new Vector2(next.X, next.Y);
            e.Use();
        }
        else if (e.rawType == EventType.MouseUp) { GUIUtility.hotControl = 0; _dragControl = 0; e.Use(); }
    }

    private void DrawSettings(Rect rect)
    {
        // Draw last, and intercept table input while open, so a toggle cannot click a row/header underneath.
        Fill(rect, Surface); _skin.Art(rect, "panel_blue_30_with_corners"); Frame(rect, Muted);
        Text(new Rect(rect.x + 18, rect.y + 14, rect.width - 36, 26), "SETTINGS", Color.white, _heading);
        var groups = new[] {
            ("ATTACK", (bool?)true, new[] { ("Kills", Settings.Kills), ("Damage", Settings.Dealt),
                ("Overkill", Settings.TableDamageOverkill), ("Core dmg", Settings.CoreDamage), ("Hacked", Settings.Hacking),
                ("Hack amount", Settings.TableHackAmount), ("Failed hack", Settings.TableFailedHack),
                ("Source breakdowns", Settings.Sources) }),
            ("DEFENSE", (bool?)false, new[] { ("Tanked", Settings.Tanked), ("Overkill", Settings.TableTankedOverkill) }),
            ("XP", (bool?)null, new[] { ("Gained", Settings.TableXpGained), ("Fed", Settings.TableXpFed) }),
            ("UNITS", (bool?)null, new[] { ("Supply", Settings.Costs), ("Individual units", DisplaySettings.TableIndividuals), ("Levels", Settings.Levels) }) };
        var y = rect.y + 50;
        foreach (var (title, attack, options) in groups)
        {
            Fill(new Rect(rect.x + 10, y, rect.width - 20, 28), new Color(0.12f, 0.17f, 0.24f));
            if (attack is { } atk) _skin.StatIcon(new Rect(rect.x + 18, y + 2, 24, 24), atk);
            else _skin.Art(new Rect(rect.x + 18, y + 2, 24, 24), title == "XP" ? "icon_exp" : "Icon_Count");
            Text(new Rect(rect.x + 50, y + 3, 240, 24), title, Color.white, _small);
            y += 34;
            foreach (var (label, entry) in options)
            {
                Text(new Rect(rect.x + 30, y + 4, 218, 28), label, Muted, _label);
                if (Button(new Rect(rect.x + 256, y + 2, 72, 28), entry.Value ? "ON" : "OFF", entry.Value))
                { entry.Value = !entry.Value; _tableScroll = Vector2.zero; }
                y += 32;
            }
        }
        if (rect.Contains(Event.current.mousePosition) && Event.current.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel) Event.current.Use();
    }

    private static void Wheel(Rect viewport, ref Vector2 scroll, float maxX, float maxY)
    {
        scroll.x = Math.Clamp(scroll.x, 0, maxX); scroll.y = Math.Clamp(scroll.y, 0, maxY);
        var e = Event.current;
        if (!GUI.enabled || e.type != EventType.ScrollWheel || !viewport.Contains(e.mousePosition)) return;
        if (e.shift) scroll.x = Math.Clamp(scroll.x + e.delta.y * 32, 0, maxX);
        else { scroll.x = Math.Clamp(scroll.x + e.delta.x * 32, 0, maxX); scroll.y = Math.Clamp(scroll.y + e.delta.y * 32, 0, maxY); }
        e.Use();
    }
    private static void Scrollbar(Rect track, ref float offset, float visible, float content, bool horizontal, int hint,
        Vector2? capturedPointer = null)
    {
        var maximum = Math.Max(0, content - visible); offset = Math.Clamp(offset, 0, maximum);
        var id = GUIUtility.GetControlID(hint, FocusType.Passive); var e = Event.current;
        if (maximum <= 0) { if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0; return; }
        var length = horizontal ? track.width : track.height;
        var thumbLength = Math.Min(length, Math.Max(24, length * visible / content)); var travel = length - thumbLength;
        if (GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && track.Contains(e.mousePosition))
        { GUIUtility.hotControl = id; e.Use(); }
        if (GUIUtility.hotControl == id)
        {
            if (e.rawType is EventType.MouseDown or EventType.MouseDrag)
            {
                var pointer = capturedPointer ?? e.mousePosition;
                var position = horizontal ? pointer.x - track.x : pointer.y - track.y;
                offset = travel <= 0 ? 0 : Math.Clamp((position - thumbLength / 2) / travel, 0, 1) * maximum;
                if (e.type != EventType.Used) e.Use();
            }
            else if (e.rawType == EventType.MouseUp) { GUIUtility.hotControl = 0; if (e.type != EventType.Used) e.Use(); }
        }
        Fill(track, new Color(0.12f, 0.16f, 0.22f));
        var thumb = horizontal ? new Rect(track.x + travel * offset / maximum, track.y, thumbLength, track.height)
            : new Rect(track.x, track.y + travel * offset / maximum, track.width, thumbLength);
        Fill(thumb, Muted);
    }
    private void InitStyles()
    {
        if (_label is not null) return;
        _skin.Load();
        var font = _skin.Body ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // GUIStyle's copy constructor is stripped in this game's IL2CPP build.
        _label = new GUIStyle { font = font, fontSize = 17, clipping = TextClipping.Clip, wordWrap = false };
        _small = new GUIStyle { font = font, fontSize = 14, clipping = TextClipping.Clip, wordWrap = false };
        _unit = new GUIStyle { font = font, fontSize = 15, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = true };
        _unit.normal.textColor = Color.white;
        _number = new GUIStyle { font = font, fontSize = 15, alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip, wordWrap = false };
        _value = new GUIStyle { font = font, fontSize = 20, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, wordWrap = false };
        _caption = new GUIStyle { font = font, fontSize = 12, clipping = TextClipping.Clip, wordWrap = false };
        _value.normal.textColor = _caption.normal.textColor = Color.white;
        _heading = new GUIStyle { font = _skin.Title ?? font, fontSize = 18, clipping = TextClipping.Clip };
        _button = new GUIStyle { font = font, fontSize = 16, alignment = TextAnchor.MiddleCenter };
        _label.normal.textColor = _small.normal.textColor = _heading.normal.textColor = Color.white;
        _number.normal.textColor = Color.white;
        _button.normal.textColor = _button.hover.textColor = _button.active.textColor = Color.white;
    }

    private bool Button(Rect rect, string text, bool selected = false)
    {
        var before = GUI.backgroundColor;
        try
        {
            var hover = rect.Contains(Event.current.mousePosition);
            GUI.backgroundColor = Color.white;
            var name = "button_" + (selected ? "y" : "b") + (rect.width < 70 ? "_x" : "_m") + (hover ? "2" : "1");
            if (!_skin.Art(rect, name))
            {
                Fill(rect, selected ? new Color(0.34f, 0.28f, 0.15f) : new Color(0.13f, 0.18f, 0.27f));
                Frame(rect, selected ? Gold : Muted);
            }
            return GUI.Button(rect, text, _button!);
        }
        finally { GUI.backgroundColor = before; }
    }

    private static void Frame(Rect rect, Color color)
    {
        Fill(new Rect(rect.x + 3, rect.y, rect.width - 6, 1), color);
        Fill(new Rect(rect.x + 3, rect.yMax - 1, rect.width - 6, 1), color);
        Fill(new Rect(rect.x, rect.y + 3, 1, rect.height - 6), color);
        Fill(new Rect(rect.xMax - 1, rect.y + 3, 1, rect.height - 6), color);
        Fill(new Rect(rect.x + 1, rect.y + 1, 2, 2), color);
        Fill(new Rect(rect.xMax - 3, rect.y + 1, 2, 2), color);
        Fill(new Rect(rect.x + 1, rect.yMax - 3, 2, 2), color);
        Fill(new Rect(rect.xMax - 3, rect.yMax - 3, 2, 2), color);
    }

    private static void Fill(Rect rect, Color color)
    {
        var before = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = before;
    }

    private static void Text(Rect rect, string text, Color color, GUIStyle? style)
    {
        var before = GUI.color;
        GUI.color = color;
        GUI.Label(rect, text, style!);
        GUI.color = before;
    }

    private static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    private static string Compact(long n) => n >= 1_000_000 ? (n / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "m"
        : n >= 1000 ? (n / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k" : Number(n);
    private static string Compact(double n) => n >= 1_000_000 ? (n / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "m"
        : n >= 1000 ? (n / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k" : n.ToString("0.#", CultureInfo.InvariantCulture);
    public void Dispose() { _skin.Dispose(); _launcher.Dispose(); Object.Destroy(_blocker); }
}
