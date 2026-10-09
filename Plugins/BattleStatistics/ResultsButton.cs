using GameRiver.Client;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Both result actions and the battlefield menu use their native button art,
// layout and initialized interactive effects.
internal sealed class ResultsButton : IDisposable
{
    private readonly Action _open;
    private Il2CppSystem.Action? _click;
    private BattleResultWindow? _window;
    private InteractiveUIComponent? _button;
    private float _retryAt;
    private InGameOptionWindow? _menu;
    private InteractiveUIComponent? _menuButton;
    private Il2CppSystem.Action? _menuClick;
    private float _menuRetryAt;
    private readonly Dictionary<long, (RectTransform Rect, Vector2 Position, Vector2 Size)> _menuPositions = new();

    internal ResultsButton(Action open) => _open = open;

    internal void WindowShown(GRWindow window, bool available)
    {
        if (window.TryCast<InGameOptionWindow>() is { } menu)
        {
            if (_menu == null || _menu.Pointer != menu.Pointer) DisposeMenu();
            _menu = menu;
            _menuRetryAt = 0;
            RefreshMenu(available);
        }
        else if (window.TryCast<BattleResultWindow>() is { } result)
        {
            if (_window == null || _window.Pointer != result.Pointer) DisposeResult();
            _window = result;
            _retryAt = 0;
            RefreshResult(available);
        }
    }

    internal void Refresh(bool available)
    {
        RefreshResult(available);
        RefreshMenu(available);
    }

    private void RefreshResult(bool available)
    {
        if (!available)
        {
            if (_button != null) _button.gameObject.SetActive(false);
            return;
        }
        try
        {
            if (_window == null || !_window.gameObject.activeInHierarchy)
            {
                if (Time.unscaledTime < _retryAt) return;
                _retryAt = Time.unscaledTime + 1;
                var window = Resources.FindObjectsOfTypeAll<BattleResultWindow>()
                    .FirstOrDefault(w => w != null && w.gameObject.activeInHierarchy);
                if (window == null) return;
                if (_window == null || _window.Pointer != window.Pointer) DisposeResult();
                _window = window;
            }
            var buttons = new[] { _window.exitButton, _window.saveReplayButton, _window.closeButton,
                _window.retryManagedReplayButton, _window.exitManagedReplayButton }
                .Where(b => b != null && b.gameObject.activeInHierarchy).ToArray();
            var template = buttons.FirstOrDefault();
            if (template == null) return;
            if (_button == null)
            {
                _button = CreateButton(template, "MechaCommunityMod_Stats", _open, out _click);
                BattleStatisticsPlugin.Logger.LogInfo("Added Stats to the native result-window actions.");
            }
            Position(buttons, template);
            _button.gameObject.SetActive(true);
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _retryAt)
            {
                _retryAt = Time.unscaledTime + 15;
                BattleStatisticsPlugin.Logger.LogWarning("Native Stats button: " + e.Message);
            }
            if (_button != null) _button.gameObject.SetActive(false);
        }
    }

    internal static InteractiveUIComponent CreateButton(InteractiveUIComponent template, string name, Action open, out Il2CppSystem.Action click)
    {
        using var timing = new SlowOperation("Stats button creation");
        var clone = Object.Instantiate(template.gameObject, template.transform.parent, false);
        clone.name = name;
        try
        {
            var button = clone.GetComponent<InteractiveUIComponent>();
            if (button == null) throw new InvalidOperationException("Native Stats button has no interaction component.");
            button.OnClicked = null;
            button.OnClickComponent = null;
            button.OnEnter = null;
            button.OnExit = null;
            button.OnEnterComponent = null;
            button.OnExitComponent = null;
            button.OnSelected = null;
            button.OnDeselected = null;
            button.OnSelectComponent = null;
            button.OnDeselectComponent = null;
            button.group = null;
            button.isPointerEnter = false;
            button.interactable = true;
            // Awake only refreshes group state; Start is empty. Native Init is
            // what binds and initializes the hover/press/normal effect objects.
            // Use the template's resting scale even if it was hovered at copy time.
            var restingScale = template.GetComponents<ScaleUIEffect>()
                .Select(e => e.originScale).FirstOrDefault(s => s > 0);
            if (restingScale > 0) clone.transform.localScale = Vector3.one * restingScale;
            button.Init(null);
            button.Deselect();
            button.PerformPointerExit();
            foreach (var label in clone.GetComponentsInChildren<GRText>(true))
            {
                if (label.localize != null) label.localize.enabled = false;
                label.text = "STATS";
            }
            click = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(open)
                ?? throw new InvalidOperationException("Could not bind the Stats button.");
            button.add_OnClicked(click);
            return button;
        }
        catch { Object.Destroy(clone); throw; }
    }

    private void RefreshMenu(bool available)
    {
        if (!available)
        {
            if (_menuButton != null) _menuButton.gameObject.SetActive(false);
            RestoreMenu();
            return;
        }
        try
        {
            if (_menu == null || !_menu.gameObject.activeInHierarchy)
            {
                if (Time.unscaledTime < _menuRetryAt) return;
                _menuRetryAt = Time.unscaledTime + 1;
                var menu = Resources.FindObjectsOfTypeAll<InGameOptionWindow>()
                    .FirstOrDefault(w => w != null && w.gameObject.activeInHierarchy);
                if (menu == null) return;
                if (_menu == null || _menu.Pointer != menu.Pointer) DisposeMenu();
                _menu = menu;
            }
            var template = _menu.settingButton;
            if (template == null || !template.gameObject.activeInHierarchy) return;
            if (_menuButton == null)
            {
                _menuButton = CreateButton(template, "MechaCommunityMod_MenuStats", _open, out _menuClick);
                BattleStatisticsPlugin.Logger.LogInfo("Added Stats to the battlefield menu.");
            }
            _menuButton.gameObject.SetActive(true);
            var buttons = new[] { _menu.CloseButton, _menu.giveUpButton, _menu.returnButton,
                _menu.settingButton, _menu.quitButton, _menu.quitAndSaveButton, _menu.giveUpButton_TD,
                _menu.recordButton, _menu.retryManagedReplayButton, _menu.exitManagedReplayButton };
            PositionMenu(buttons, template);
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _menuRetryAt)
            {
                _menuRetryAt = Time.unscaledTime + 15;
                BattleStatisticsPlugin.Logger.LogWarning("Battlefield Stats button: " + e.Message);
            }
            if (_menuButton != null) _menuButton.gameObject.SetActive(false);
            RestoreMenu();
        }
    }

    private void PositionMenu(InteractiveUIComponent[] buttons, InteractiveUIComponent template)
    {
        var rect = _menuButton!.GetComponent<RectTransform>();
        var source = template.GetComponent<RectTransform>();
        var parent = source.parent;
        var index = source.GetSiblingIndex();
        if (rect.GetSiblingIndex() < index) index--;
        if (rect.GetSiblingIndex() != index) rect.SetSiblingIndex(index);
        if (parent.GetComponent<LayoutGroup>() != null)
        {
            LayoutRebuilder.MarkLayoutForRebuild(parent.Cast<RectTransform>());
            return;
        }
        // Some menu variants have authored positions instead of a layout group.
        // Fit the additional row inside the existing frame, retaining order.
        var peers = buttons.Where(b => b != null && b.gameObject.activeInHierarchy)
            .Select(b => b.GetComponent<RectTransform>())
            .Where(r => r.parent.Pointer == parent.Pointer).DistinctBy(r => r.Pointer).ToArray();
        foreach (var peer in peers)
            _menuPositions.TryAdd(peer.Pointer.ToInt64(), (peer, peer.anchoredPosition, peer.sizeDelta));
        RestoreMenu();
        var ordered = peers.OrderByDescending(r => r.localPosition.y).ToList();
        var before = ordered.FindIndex(r => r.Pointer == source.Pointer);
        if (before < 0) return;
        var top = ordered.Max(r => r.localPosition.y + (1 - r.pivot.y) * r.rect.height);
        var bottom = ordered.Min(r => r.localPosition.y - r.pivot.y * r.rect.height);
        ordered.Insert(before, rect);
        var rowHeight = Math.Min(source.rect.height, (top - bottom - 4 * (ordered.Count - 1)) / ordered.Count);
        if (rowHeight <= 0) throw new InvalidOperationException("No room for a Stats menu row.");
        var step = (top - bottom - rowHeight) / (ordered.Count - 1);
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta;
        rect.localPosition = source.localPosition;
        for (var i = 0; i < ordered.Count; i++)
        {
            var peer = ordered[i];
            peer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);
            var position = peer.localPosition;
            position.y = top - i * step - (1 - peer.pivot.y) * rowHeight;
            peer.localPosition = position;
        }
    }

    private void RestoreMenu()
    {
        foreach (var state in _menuPositions.Values)
            if (state.Rect != null)
            {
                state.Rect.anchoredPosition = state.Position;
                state.Rect.sizeDelta = state.Size;
            }
    }

    private void Position(InteractiveUIComponent[] buttons, InteractiveUIComponent template)
    {
        var rect = _button!.GetComponent<RectTransform>();
        var source = template.GetComponent<RectTransform>();
        var parent = source.parent;
        var peers = buttons.Select(b => b.GetComponent<RectTransform>())
            .Where(r => r.parent.Pointer == parent.Pointer).ToArray();
        var top = peers.OrderByDescending(r => r.localPosition.y).First();
        if (parent.GetComponent<LayoutGroup>() != null)
        {
            var index = top.GetSiblingIndex();
            if (rect.GetSiblingIndex() < index) index--;
            if (rect.GetSiblingIndex() != index) rect.SetSiblingIndex(index);
            return;
        }
        // Extend the existing vertical stack by one row without moving any of
        // the game's controls. Infer its spacing from the neighbouring rows.
        var step = peers.Select(r => Math.Abs(r.localPosition.y - top.localPosition.y))
            .Where(d => d > 1).DefaultIfEmpty(top.rect.height + 8).Min();
        step = Math.Max(step, top.rect.height + 4);
        rect.anchorMin = top.anchorMin;
        rect.anchorMax = top.anchorMax;
        rect.pivot = top.pivot;
        rect.sizeDelta = top.sizeDelta;
        rect.anchoredPosition = top.anchoredPosition + new Vector2(0, step);
    }

    private void DisposeResult()
    {
        if (_button != null) Object.Destroy(_button.gameObject);
        _button = null;
        _click = null;
        _window = null;
    }

    private void DisposeMenu()
    {
        RestoreMenu();
        _menuPositions.Clear();
        if (_menuButton != null) Object.Destroy(_menuButton.gameObject);
        _menuButton = null;
        _menuClick = null;
        _menu = null;
    }

    public void Dispose() { DisposeResult(); DisposeMenu(); }
}
