using System.Globalization;
using GameRiver;
using GameRiver.Client;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed class OverlayView : IDisposable
{
    private readonly GameObject _root;
    private readonly CanvasScaler _scaler;
    private readonly Canvas _canvas;
    private readonly Font _font;
    private readonly ResultsSkin _skin = new();
    private readonly StatsHelpView _help = new(live: true);
    private readonly LiveOverlaySettingsView _settings = new();
    private readonly RectTransform _helpInput;
    private SidePanel _left;
    private SidePanel _right;
    private bool _spectating;
    private bool _visible;
    private string? _lastBlocker;
    private readonly SideHudSpace _sideHud = new();
    private static readonly Color Blue = new(0.22f, 0.48f, 0.9f, 1f);
    private static readonly Color Red = new(0.85f, 0.23f, 0.24f, 1f);

    internal OverlayView()
    {
        _root = new GameObject("MechaCommunityMod");
        Object.DontDestroyOnLoad(_root);
        try
        {
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1;
            _root.AddComponent<GraphicRaycaster>();
            _scaler = _root.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 1;
            _skin.Load();
            _font = _skin.Body ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _left = new SidePanel(_root.transform, false, Blue, _font, 246, _skin, ToggleHelp, ToggleSettings);
            _right = new SidePanel(_root.transform, true, Red, _font, 246, _skin, ToggleHelp, ToggleSettings);
            _helpInput = Rect(_root.transform, "OverlayGuideInput", 0, 0, 0, 0);
            _helpInput.anchorMin = _helpInput.anchorMax = _helpInput.pivot = new Vector2(.5f, .5f);
            Paint(_helpInput, Color.clear).raycastTarget = true;
            _helpInput.gameObject.SetActive(false);
        }
        catch
        {
            Object.Destroy(_root);
            throw;
        }
    }

    internal void SetVisible(bool visible)
    {
        _visible = visible;
        RefreshVisibility();
    }

    internal void RefreshVisibility()
    {
        var show = _visible && !OverlayUiLayer.Obscured();
        var blocker = _visible ? OverlayUiLayer.Blocker : null;
        if (blocker != _lastBlocker)
        {
            BattleStatisticsPlugin.Logger.LogInfo(blocker is null ? "Side overlay is no longer blocked by native UI."
                : "Side overlay hidden by native UI: " + blocker);
            _lastBlocker = blocker;
        }
        if (_root.activeSelf != show) _root.SetActive(show);
        if (!show) ClosePopups();
        else { _left.RefreshTooltipVisibility(_canvas); _right.RefreshTooltipVisibility(_canvas); }
    }

    internal void Refresh(OverlaySnapshot snapshot, GameReader reader)
    {
        using var timing = new SlowOperation("side panel rendering");
        var scale = BattleStatisticsPlugin.Scale.Value;
        var spectating = MatchClient.Current?.IsWatchMode() == true;
        if (_spectating != spectating)
        {
            _spectating = spectating;
            _left.Dispose();
            _right.Dispose();
            var width = spectating ? 196 : 246;
            _left = new SidePanel(_root.transform, false, Blue, _font, width, _skin, ToggleHelp, ToggleSettings);
            _right = new SidePanel(_root.transform, true, Red, _font, width, _skin, ToggleHelp, ToggleSettings);
            _helpInput.SetAsLastSibling();
        }
        OverlayUiLayer.Place(_canvas);
        _scaler.referenceResolution = new Vector2(1920f / scale, 1080f / scale);
        // Spectators use the tall lane alongside native controls. Playing uses
        // the edge until the actual lower HUD begins, independently per side.
        var bottom = (_spectating ? 940f : 900f) / scale;
        var top = Math.Clamp(BattleStatisticsPlugin.Top.Value, 100f, Math.Max(100f, bottom - 118f));
        var layout = _sideHud.Layout(top, bottom, scale, _spectating, _spectating ? 196 : 246);
        var maxRows = _spectating ? BattleStatisticsPlugin.SpectatorMaxRows.Value : BattleStatisticsPlugin.MaxRows.Value;
        _left.Refresh(snapshot.Left, top, layout.LeftBottom - top, layout.Left, reader, maxRows, snapshot.Available);
        _right.Refresh(snapshot.Right, top, layout.RightBottom - top, layout.Right, reader, maxRows, snapshot.Available);
    }

    private static Rect HelpRect() => new((Screen.width - Math.Min(720, Screen.width - 24)) / 2f,
        (Screen.height - Math.Min(640, Screen.height - 24)) / 2f,
        Math.Min(720, Screen.width - 24), Math.Min(640, Screen.height - 24));

    private void ToggleHelp()
    {
        _settings.Close();
        _help.Toggle();
        SyncHelpInput();
    }
    private static Rect SettingsRect() => new((Screen.width - Math.Min(416, Screen.width - 24)) / 2f,
        (Screen.height - Math.Min(628, Screen.height - 24)) / 2f,
        Math.Min(416, Screen.width - 24), Math.Min(628, Screen.height - 24));
    private void ToggleSettings()
    {
        _help.Close(); _settings.Toggle(); SyncHelpInput();
    }
    private void SyncHelpInput()
    {
        var rect = _settings.Open ? SettingsRect() : HelpRect();
        _helpInput.sizeDelta = rect.size / Math.Max(.01f, _canvas.scaleFactor);
        _helpInput.gameObject.SetActive((_help.Open || _settings.Open) && _root.activeSelf);
    }
    internal void DrawPopup()
    {
        if ((!_help.Open && !_settings.Open) || !_visible || !_root.activeSelf || OverlayUiLayer.Obscured()) return;
        var oldDepth = GUI.depth;
        try
        {
            GUI.depth = -9950;
            if (_settings.Open) _settings.Draw(SettingsRect(), _skin);
            else _help.Draw(HelpRect(), _skin);
            SyncHelpInput();
        }
        finally { GUI.depth = oldDepth; }
    }
    internal void ClosePopups() { _help.Close(); _settings.Close(); if (_helpInput != null) _helpInput.gameObject.SetActive(false); }
    public void Dispose() { ClosePopups(); _skin.Dispose(); Object.Destroy(_root); }

    private sealed class SidePanel
    {
        private const float RowHeight = 66;
        private static bool HasHacking(DamageRow row) => row.Hacking.Total > 0 || row.Hacking.Converted > 0;
        private static int XpLines => (LiveOverlaySettings.XpGained.Value ? 1 : 0) + (LiveOverlaySettings.XpFed.Value ? 1 : 0);
        private static float Height(DamageRow row) => RowHeight + (row.DisplayName is not null ? 18 : 0)
            + (row.Squads is { Length: > 0 } ? XpLines * 20 : 0)
            + (HasHacking(row) ? (LiveOverlaySettings.HackAmount.Value ? 22 : 0)
                + (LiveOverlaySettings.FailedHack.Value ? 22 : 0) : 0);
        private readonly float Width;
        private readonly RectTransform _rect;
        private readonly CanvasGroup _visibility;
        private readonly RectTransform _edge;
        private readonly bool _right;
        private readonly Color _accent;
        private readonly Font _font;
        private readonly List<RowView> _rows = new();
        private readonly RectTransform _pager;
        private readonly Text _pageLabel;
        private readonly Text _toggleHint;
        private readonly Text _emptyState;
        private int _page, _pageCount;

        internal SidePanel(Transform parent, bool right, Color accent, Font font, float width, ResultsSkin skin, Action help, Action settings)
        {
            Width = width;
            _right = right;
            _accent = accent;
            _font = font;
            _rect = Rect(parent, right ? "RightDamage" : "LeftDamage", 0, 0, Width, 0);
            _visibility = _rect.gameObject.AddComponent<CanvasGroup>();
            _rect.anchorMin = _rect.anchorMax = new Vector2(right ? 1 : 0, 1);
            _rect.pivot = new Vector2(right ? 1 : 0, 1);
            Paint(_rect, new Color(0.035f, 0.045f, 0.075f, 0.91f));
            _edge = Rect(_rect, "Edge", right ? Width - 2 : 0, 0, 2, 0);
            Paint(_edge, accent);
            _toggleHint = Label(_rect, "ToggleHint", 6, 4, Width - 106, 20, 12, font, TextAnchor.MiddleCenter);
            _toggleHint.color = new Color(0.65f, 0.71f, 0.8f);
            HeaderButton(_rect, Width - 94, 60, "Settings", skin, settings);
            HeaderButton(_rect, Width - 28, 22, "?", skin, help);
            _emptyState = Label(_rect, "WaitingForStats", 10, 30, Width - 20, 36, 13, font, TextAnchor.MiddleCenter);
            _emptyState.color = new Color(.74f, .8f, .9f);
            _emptyState.enabled = false;
            _pager = Rect(_rect, "Pages", 6, 0, Width - 12, 24);
            PageButton(_pager, 0, "<", () => _page = Math.Max(0, _page - 1));
            PageButton(_pager, Width - 44, ">", () => _page = Math.Min(_pageCount - 1, _page + 1));
            _pageLabel = Label(_pager, "Page", 34, 0, Width - 80, 24, 12, font, TextAnchor.MiddleCenter);
            _pager.gameObject.SetActive(false);
        }

        private void HeaderButton(Transform parent, float x, float width, string caption, ResultsSkin skin, Action action)
        {
            var helpRect = Rect(parent, "Header" + caption, x, 3, width, 20);
            var helpArt = Paint(helpRect, Color.white);
            helpArt.sprite = skin.GameSprite("button_b_x1");
            if (helpArt.sprite == null) helpArt.color = new Color(.12f, .2f, .31f, 1);
            helpArt.raycastTarget = true;
            var helpButton = helpRect.gameObject.AddComponent<Button>();
            helpButton.targetGraphic = helpArt;
            if (helpArt.sprite != null && skin.GameSprite("button_b_x2") is { } highlighted)
            {
                helpButton.transition = Selectable.Transition.SpriteSwap;
                helpButton.spriteState = new SpriteState { highlightedSprite = highlighted, pressedSprite = highlighted, selectedSprite = highlighted };
            }
            helpButton.onClick.AddListener((UnityEngine.Events.UnityAction)action);
            Label(helpRect, "Caption", 0, 0, width, 20, caption == "?" ? 16 : 12, skin.Body ?? _font, TextAnchor.MiddleCenter).text = caption;
        }

        internal void Dispose() => Object.Destroy(_rect.gameObject);

        internal void RefreshTooltipVisibility(Canvas canvas)
        {
            var visible = !OverlayUiLayer.TooltipOverlaps(OverlayUiLayer.ScreenBounds(_rect, canvas));
            _visibility.alpha = visible ? 1 : 0;
            _visibility.interactable = visible;
            _visibility.blocksRaycasts = visible;
        }

        private void PageButton(Transform parent, float x, string caption, Action click)
        {
            var rect = Rect(parent, "Page" + caption, x, 0, 32, 24);
            var background = Paint(rect, new Color(0.12f, 0.2f, 0.31f, 1));
            background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)click);
            Label(rect, "Arrow", 0, 0, 32, 24, 16, _font, TextAnchor.MiddleCenter).text = caption;
        }

        internal void Refresh(TeamStats stats, float top, float room, float inset, GameReader reader, int maxRows, bool available)
        {
            // Include every unit type, even rows hidden by the display limit.
            var dealtTotal = stats.TotalDealt;
            var takenTotal = stats.TotalDamageTaken;
            // Compare both hacking outcomes on one scale, across every page.
            // Active progress is still pending, never shown as a failed attempt.
            var hackTotal = stats.Rows.Sum(r => (double)Math.Max(0, r.Hacking.Successful) + Math.Max(0, r.Hacking.Failed));
            _rect.anchoredPosition = new Vector2(_right ? -inset : inset, -top);
            _toggleHint.text = $"{BattleStatisticsPlugin.ToggleKey.Value} to hide";
            var pages = new List<(int Start, int Count, float Height)>();
            for (var start = 0; start < stats.Rows.Length;)
            {
                var countOnPage = 0;
                var pageHeight = 28f;
                while (start + countOnPage < stats.Rows.Length && countOnPage < maxRows)
                {
                    var next = Height(stats.Rows[start + countOnPage]);
                    if (countOnPage > 0 && pageHeight + next + 26 > room) break;
                    pageHeight += next;
                    countOnPage++;
                }
                pages.Add((start, countOnPage, pageHeight));
                start += countOnPage;
            }
            _pageCount = pages.Count;
            _page = Math.Clamp(_page, 0, Math.Max(0, _pageCount - 1));
            var page = pages.Count > 0 ? pages[_page] : (Start: 0, Count: 0, Height: 28f);
            var count = page.Count;
            var height = count == 0 ? 74 : page.Height + (_pageCount > 1 ? 26 : 0);
            _emptyState.enabled = count == 0;
            _emptyState.text = available ? "No combat stats yet" : "Waiting for combat stats";
            // Keep the F8 hint and settings available before native statistics
            // begin recording, including when joining a spectator match.
            // The first row may exceed the remaining space (large XP/hack
            // breakdowns or a raised unit menu). Never draw through native UI.
            _rect.gameObject.SetActive(height <= room);
            while (_rows.Count < count)
                _rows.Add(new RowView(_rect, _rows.Count, _right, _accent, _font, Width));
            var y = 24f;
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].SetVisible(i < count);
                if (i < count)
                {
                    var row = stats.Rows[page.Start + i];
                    _rows[i].Position(y, Height(row));
                    _rows[i].Refresh(row, reader.Describe(row.Unit), dealtTotal, takenTotal, hackTotal);
                    y += Height(row);
                }
            }
            _rect.sizeDelta = new Vector2(Width, height);
            _edge.sizeDelta = new Vector2(2, height);
            _pager.anchoredPosition = new Vector2(6, -page.Height);
            _pager.gameObject.SetActive(_pageCount > 1);
            _pageLabel.text = $"{_page + 1} / {_pageCount}";
        }

        private sealed class RowView
        {
            private const float BarHeight = 6;
            private readonly float Width;
            private readonly float BarWidth;
            private readonly RectTransform _root;
            private readonly RectTransform _bar;
            private readonly RectTransform _dealtOverkillBar;
            private readonly RectTransform _takenBar;
            private readonly RectTransform _overkillBar;
            private readonly RectTransform[] _dealtParts = new RectTransform[4];
            private readonly RectTransform[] _takenParts = new RectTransform[4];
            private readonly bool _right;
            private readonly Image _portrait;
            private readonly Image _portraitBackground;
            private readonly Text _effectIcon;
            private readonly Text _damage;
            private readonly Text _taken;
            private readonly Text _damageExcess, _takenExcess;
            private readonly RectTransform _damageMarker, _takenMarker;
            private readonly Text _kills;
            private readonly Text _hackSuccess;
            private readonly Text _hackFailed;
            private readonly RectTransform _hackSuccessTrack, _hackSuccessBar, _hackFailedTrack, _hackFailedBar;
            private readonly Text _name, _xp;
            private string? _portraitName;

            internal RowView(Transform parent, int index, bool right, Color accent, Font font, float width)
            {
                Width = width;
                BarWidth = width - 72;
                _right = right;
                _root = Rect(parent, "UnitRow" + index, 6, 4 + index * RowHeight, Width - 12, RowHeight);
                var iconX = right ? Width - 60 : 0;
                var textX = right ? 0 : 54;
                var icon = Rect(_root, "Portrait", iconX, 7, 48, 48);
                _portraitBackground = Paint(icon, new Color(accent.r, accent.g, accent.b, 0.2f));
                _portrait = Rect(icon, "Image", 1, 1, 46, 46).gameObject.AddComponent<Image>();
                _portrait.raycastTarget = false;
                _portrait.preserveAspect = true;
                _portrait.enabled = false;
                _effectIcon = Label(icon, "EffectIcon", 0, 0, 48, 48, 32, font, TextAnchor.MiddleCenter);
                _effectIcon.text = "◆";
                _effectIcon.enabled = false;
                // Values sit above the fill on a neutral backing. Number columns
                // retain the same reading order on both sides of the battlefield.
                var track = Rect(_root, "Track", textX, 26, BarWidth, 24);
                Paint(track, new Color(.06f, .075f, .105f, .8f));
                var damageFill = Rect(track, "BarTrack", 0, 18, BarWidth, BarHeight);
                Paint(damageFill, new Color(.14f, .16f, .21f, 1));
                _bar = Rect(damageFill, "DamageBar", 0, 0, 0, BarHeight);
                if (right)
                {
                    _bar.anchorMin = _bar.anchorMax = new Vector2(1, 1);
                    _bar.pivot = new Vector2(1, 1);
                }
                Paint(_bar, DamagePalette.ColorFor(DamageCategory.Attack, true));
                _dealtOverkillBar = Rect(_bar, "DealtOverkill", 0, 0, 0, BarHeight);
                _dealtOverkillBar.anchorMin = _dealtOverkillBar.anchorMax = _dealtOverkillBar.pivot = new Vector2(right ? 0 : 1, 1);
                Paint(_dealtOverkillBar, DamagePalette.DamageOverkill);
                _damage = Label(track, "Damage", 5, 0, BarWidth - 10, 18, 14, font, TextAnchor.MiddleLeft);
                _damage.fontStyle = FontStyle.Normal;
                _damageExcess = Label(track, "DamageOverkillValue", 5, 0, BarWidth - 10, 18, 14, font, TextAnchor.MiddleRight);
                _damageExcess.fontStyle = FontStyle.Normal;
                _damageMarker = Rect(track, "OverkillMarker", 0, 7, 5, 5);
                Paint(_damageMarker, DamagePalette.DamageOverkill);
                var takenTrack = Rect(_root, "TakenTrack", textX, 1, BarWidth, 24);
                Paint(takenTrack, new Color(.06f, .075f, .105f, .8f));
                var takenFill = Rect(takenTrack, "BarTrack", 0, 18, BarWidth, BarHeight);
                Paint(takenFill, new Color(.14f, .16f, .21f, 1));
                _takenBar = Rect(takenFill, "DamageTakenBar", 0, 0, 0, BarHeight);
                if (right)
                {
                    _takenBar.anchorMin = _takenBar.anchorMax = new Vector2(1, 1);
                    _takenBar.pivot = new Vector2(1, 1);
                }
                Paint(_takenBar, DamagePalette.ColorFor(DamageCategory.Attack, false));
                for (var category = 0; category < 4; category++)
                {
                    _dealtParts[category] = SourcePart(_bar, category, true, right, BarHeight);
                    _takenParts[category] = SourcePart(_takenBar, category, false, right, BarHeight);
                }
                _dealtOverkillBar.SetAsLastSibling();
                _overkillBar = Rect(_takenBar, "Overkill", 0, 0, 0, BarHeight);
                // Attach to the far end of the blue fill, mirrored on the right.
                _overkillBar.anchorMin = _overkillBar.anchorMax = _overkillBar.pivot = new Vector2(right ? 0 : 1, 1);
                Paint(_overkillBar, DamagePalette.TankedOverkill);
                _taken = Label(takenTrack, "DamageTaken", 5, 0, BarWidth - 10, 18, 14, font, TextAnchor.MiddleLeft);
                _taken.fontStyle = FontStyle.Normal;
                _takenExcess = Label(takenTrack, "TankedOverkillValue", 5, 0, BarWidth - 10, 18, 14, font, TextAnchor.MiddleRight);
                _takenExcess.fontStyle = FontStyle.Normal;
                _takenMarker = Rect(takenTrack, "OverkillMarker", 0, 7, 5, 5);
                Paint(_takenMarker, DamagePalette.TankedOverkill);
                _kills = Label(_root, "Kills", textX, 51, BarWidth, 14, 12, font, TextAnchor.MiddleLeft);
                (_hackSuccessTrack, _hackSuccessBar, _hackSuccess) = HackBar("SuccessfulHacking", textX, right, font,
                    DamagePalette.HackAmount);
                (_hackFailedTrack, _hackFailedBar, _hackFailed) = HackBar("FailedHacking", textX, right, font,
                    DamagePalette.FailedHack);
                _name = Label(_root, "SquadName", 0, RowHeight, Width - 12, 17, 13, font, TextAnchor.MiddleLeft);
                _xp = Label(_root, "Experience", 0, RowHeight + 18, Width - 12, 36, 13, font, TextAnchor.MiddleLeft);
            }

            internal void SetVisible(bool visible) => _root.gameObject.SetActive(visible);
            internal void Position(float y, float height)
            {
                _root.anchoredPosition = new Vector2(6, -y);
                _root.sizeDelta = new Vector2(Width - 12, height);
            }

            private (RectTransform Track, RectTransform Bar, Text Value) HackBar(string name, float x, bool right, Font font, Color color)
            {
                var track = Rect(_root, name + "Track", x, RowHeight, BarWidth, 22);
                Paint(track, new Color(.06f, .075f, .105f, .8f));
                var fill = Rect(track, "BarTrack", 0, 16, BarWidth, BarHeight);
                Paint(fill, new Color(.14f, .16f, .21f, 1));
                var bar = Rect(fill, name + "Bar", 0, 0, 0, BarHeight);
                if (right)
                {
                    bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1, 1);
                }
                Paint(bar, color);
                var value = Label(track, name, 5, 0, BarWidth - 10, 16, 12, font, TextAnchor.MiddleLeft);
                value.fontStyle = FontStyle.Normal;
                return (track, bar, value);
            }

            internal void Refresh(DamageRow row, UnitDisplay display, long dealtTotal, long takenTotal, double hackTotal)
            {
                var splitDealt = LiveOverlaySettings.DamageOverkill.Value;
                var splitTaken = LiveOverlaySettings.TankedOverkill.Value;
                var dealt = row.TotalDealt;
                var taken = row.DamageTaken;
                _damage.transform.parent.gameObject.SetActive(LiveOverlaySettings.Damage.Value);
                _taken.transform.parent.gameObject.SetActive(LiveOverlaySettings.Tanked.Value);
                var showHacked = LiveOverlaySettings.Hacked.Value && HasHacking(row);
                _kills.enabled = LiveOverlaySettings.Kills.Value || showHacked;
                BarValues(_damage, _damageExcess, _damageMarker, row.Damage, row.DealtOverkill, splitDealt);
                BarValues(_taken, _takenExcess, _takenMarker, row.DamageTaken - row.Overkill, row.Overkill, splitTaken);
                _kills.text = LiveOverlaySettings.Kills.Value ? $"\u00d7 {Number(row.Kills)}" : "";
                if (LiveOverlaySettings.Kills.Value && row.Unit.Kind == 100 + (int)DamageCategory.Spell) _kills.text += $"   Uses {Number(row.Casts)}";
                if (showHacked) _kills.text += (_kills.text.Length > 0 ? "   " : "") + $"Hacked {Number(row.Hacking.Converted)}";
                _bar.sizeDelta = new Vector2(BarWidth * DamageModel.BarFraction(dealt, dealtTotal), BarHeight);
                _dealtOverkillBar.sizeDelta = new Vector2(BarWidth * DamageModel.BarFraction(row.DealtOverkill, dealtTotal), BarHeight);
                _takenBar.sizeDelta = new Vector2(BarWidth * DamageModel.BarFraction(taken, takenTotal), BarHeight);
                _overkillBar.sizeDelta = new Vector2(BarWidth * DamageModel.BarFraction(row.Overkill, takenTotal), BarHeight);
                RefreshSources(_dealtParts, row.Sources(true), dealtTotal, BarHeight, splitDealt);
                RefreshSources(_takenParts, row.Sources(false), takenTotal, BarHeight, splitTaken);
                _dealtOverkillBar.gameObject.SetActive(splitDealt);
                _overkillBar.gameObject.SetActive(splitTaken);
                foreach (var part in _dealtParts.Concat(_takenParts)) part.gameObject.SetActive(LiveOverlaySettings.SourceColors.Value);
                _name.enabled = row.DisplayName is not null; _name.text = row.DisplayName ?? "";
                var y = _name.enabled ? RowHeight + 18 : RowHeight;
                _xp.enabled = XpLines > 0 && row.Squads is { Length: > 0 };
                _xp.rectTransform.anchoredPosition = new Vector2(0, -y);
                _xp.rectTransform.sizeDelta = new Vector2(Width - 12, XpLines * 20);
                _xp.text = string.Join("\n", new[] {
                    LiveOverlaySettings.XpGained.Value ? $"XP gained  {row.XpEarned:0.#}" : null,
                    LiveOverlaySettings.XpFed.Value ? $"XP fed  {row.EnemyXpAwarded:0.#}" : null }.Where(s => s is not null));
                if (_xp.enabled) y += XpLines * 20;
                var showHackAmount = LiveOverlaySettings.HackAmount.Value && HasHacking(row);
                var showFailedHack = LiveOverlaySettings.FailedHack.Value && HasHacking(row);
                _hackSuccessTrack.gameObject.SetActive(showHackAmount);
                _hackFailedTrack.gameObject.SetActive(showFailedHack);
                var textX = _right ? 0 : 54;
                _hackSuccessTrack.anchoredPosition = new Vector2(textX, -y);
                if (showHackAmount) y += 22;
                _hackFailedTrack.anchoredPosition = new Vector2(textX, -y);
                _hackSuccessBar.sizeDelta = new Vector2(BarWidth * DamageModel.BarFraction(row.Hacking.Successful, hackTotal), BarHeight);
                _hackFailedBar.sizeDelta = new Vector2(BarWidth * DamageModel.BarFraction(row.Hacking.Failed, hackTotal), BarHeight);
                _hackSuccess.text = "Hack amount " + (row.Hacking.Successful > 0 ? Compact(row.Hacking.Successful) : "-");
                _hackFailed.text = "Failed hack " + (row.Hacking.Failed > 0 ? Compact(row.Hacking.Failed) : "-");
                // Match UnitIconMini.RefreshSmallPortrait: the setting name needs
                // the Mech selector and level suffix, not a plain sprite lookup.
                // Retry after loading; a null sprite must never draw a white box.
                if (_portraitName != display.Portrait || _portrait.sprite == null)
                {
                    _portraitName = display.Portrait;
                    _portrait.sprite = string.IsNullOrWhiteSpace(_portraitName) ? null
                        : display.DirectPortrait ? GRUIManager.Instance.GetSpriteManager().GetSprite(_portraitName)
                        : GRUIManager.Instance.GetSpriteManager().GetSprite(
                            _portraitName, display.PortraitType ?? SpriteType.Mech,
                            display.PortraitType is null ? (int)CardLevel.Level1 : 0);
                }
                if (display.Effect && row.Unit.Kind is 101 or 102)
                    _portrait.sprite = EffectCatalog.Portrait((DamageCategory)(row.Unit.Kind - 100), row.Unit.Id);
                _portrait.enabled = _portrait.sprite != null;
                // Building sprites already contain their own silhouette shading.
                // Let their transparent edges sit on the panel without an army-tinted box.
                _portraitBackground.enabled = row.Unit.Kind != (int)GameRiver.Fight.DamageRecorderType.Construction;
                _effectIcon.enabled = display.Effect && _portrait.sprite == null;
                if (display.Effect) _effectIcon.color = DamagePalette.ColorFor((DamageCategory)(row.Unit.Kind - 100), true);
            }

            private static string Compact(long value) => value >= 1000000 ? (value / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "m"
                : value >= 1000 ? (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k" : Number(value);

            private void BarValues(Text main, Text excess, RectTransform marker, long effective, long overkill, bool split)
            {
                var separate = split && overkill > 0;
                var available = BarWidth - 10;
                var width = separate ? (available - 16) / 2 : available;
                main.rectTransform.anchoredPosition = new Vector2(5, 0);
                main.rectTransform.sizeDelta = new Vector2(width, main.rectTransform.sizeDelta.y);
                main.text = Compact(split ? effective : effective + overkill);
                excess.enabled = separate;
                excess.rectTransform.anchoredPosition = new Vector2(5 + available - width, 0);
                excess.rectTransform.sizeDelta = new Vector2(width, excess.rectTransform.sizeDelta.y);
                excess.text = separate ? Compact(overkill) : "";
                marker.gameObject.SetActive(separate);
                if (separate)
                    marker.anchoredPosition = new Vector2(5 + available - Math.Min(width, excess.preferredWidth) - 9, -7);
            }

            private static RectTransform SourcePart(RectTransform parent, int category, bool dealt, bool right, float height)
            {
                var part = Rect(parent, "Source" + category, 0, 0, 0, height);
                part.anchorMin = part.anchorMax = part.pivot = new Vector2(right ? 1 : 0, 1);
                Paint(part, DamagePalette.ColorFor((DamageCategory)category, dealt));
                return part;
            }

            private void RefreshSources(RectTransform[] parts, SourceDamage[] sources, long total, float height, bool splitOverkill)
            {
                float offset = 0;
                for (var category = 0; category < parts.Length; category++)
                {
                    var amount = sources.Where(s => (int)s.Source.Category == category).Sum(s => s.Damage + (splitOverkill ? 0 : s.Overkill));
                    var width = BarWidth * DamageModel.BarFraction(amount, total);
                    parts[category].anchoredPosition = new Vector2(_right ? -offset : offset, 0);
                    parts[category].sizeDelta = new Vector2(width, height);
                    offset += width;
                }
            }
        }
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static Image Paint(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text Label(Transform parent, string name, float x, float y, float width,
        float height, int size, Font font, TextAnchor alignment)
    {
        var text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }
}
