using GameRiver.Client;
using GameRiver;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Native windows own the foreground. Cache window references on Show, then
// inspect only their active state; never scan the scene on every frame.
internal static class OverlayUiLayer
{
    private sealed record Entry(GRWindow Window, GameObject? Content, bool Special, bool Reinforcement, bool Tooltip, bool ModalLayer, string Description);
    private static readonly List<Entry> Windows = new();
    private static bool _bootstrapped;
    private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> Corners = new(4);
    internal static string? Blocker { get; private set; }

    internal static void Track(GRWindow window)
    {
        GameObject? content = null;
        var layer = window.GetWindowType();
        var special = window.TryCast<InGameOptionWindow>() is not null || window.TryCast<BattleResultWindow>() is not null
            || window.TryCast<OptionWindow>() is not null || layer == GRUIManager.EWindowType.System_Loading;
        var reinforcement = false;
        var tooltip = window.TryCast<TooltipWindow>() is not null;
        if (window.TryCast<ChooseReinforcementWatchWindow>() is { } watch)
        {
            content = watch.reinforcementPanel;
            reinforcement = true;
        }
        else if (window.TryCast<ChooseReinforcementWindow>() is { } choice)
        {
            content = choice.itemNode?.gameObject;
            reinforcement = true;
        }
        else if (!special && !tooltip && layer is not (GRUIManager.EWindowType.Game_Window
            or GRUIManager.EWindowType.System_Popup or GRUIManager.EWindowType.System_Loading)) return;
        var entry = new Entry(window, content, special, reinforcement, tooltip,
            layer is GRUIManager.EWindowType.Game_Window or GRUIManager.EWindowType.System_Popup or GRUIManager.EWindowType.System_Loading,
            (window.GetWindowId() ?? window.gameObject.name) + " / " + layer);
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (Windows[i].Window == null) Windows.RemoveAt(i);
            else if (Windows[i].Window.Pointer == window.Pointer)
            {
                Windows[i] = entry;
                return;
            }
        }
        Windows.Add(entry);
    }
    internal static void Hidden(GRWindow window)
    {
        for (var i = Windows.Count - 1; i >= 0; i--)
            if (Windows[i].Window == null || Windows[i].Window.Pointer == window.Pointer) Windows.RemoveAt(i);
    }

    internal static bool Obscured(bool includeTooltips = false)
    {
        Blocker = null;
        if (!_bootstrapped)
        {
            _bootstrapped = true;
            foreach (var window in Resources.FindObjectsOfTypeAll<GRWindow>())
                if (window != null && window.gameObject.scene.IsValid()) Track(window);
        }
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            var entry = Windows[i];
            if (entry.Window == null) { Windows.RemoveAt(i); continue; }
            if (entry.Tooltip && !includeTooltips) continue;
            var window = entry.Window;
            // The tooltip window is pooled and can remain active with no visible
            // panel. Read its current content rather than its cached root state.
            var contentActive = entry.Tooltip
                ? window.TryCast<TooltipWindow>()?.currentPanel?.gameObject.activeInHierarchy == true
                : entry.Content?.activeInHierarchy ?? false;
            // Root GameObjects can stay active when a native window hides its canvas.
            // Notifications and background-free HUD windows are not modal blockers.
            if (OverlayWindowPolicy.Blocks(entry.Special, entry.Reinforcement, entry.ModalLayer,
                window.backgroundType is not (WindowBackgroundType.Disable or WindowBackgroundType.TouchOnly),
                window.gameObject.activeInHierarchy, window._canvas == null || window._canvas.isActiveAndEnabled,
                contentActive, entry.Tooltip))
            {
                Blocker = entry.Description;
                return true;
            }
        }
        return false;
    }

    internal static void Place(Canvas canvas)
    {
        var ui = GRUIManager.Instance;
        if (ui == null) return;
        var hud = ui.GetLayer(GRUIManager.EWindowType.Game_MainScene)?.GetTopWindow()?.TryCast<GRWindow>()?._canvas;
        var native = hud != null ? hud : ui.GetCanvas2D();
        if (native == null) return;
        canvas.sortingLayerID = native.sortingLayerID;
        canvas.overrideSorting = true;
        canvas.sortingOrder = Math.Max(native.sortingOrder, ui.GetCanvas2D()?.sortingOrder ?? native.sortingOrder) + 1;
    }

    internal static Rect ScreenBounds(RectTransform rect, Canvas? canvas)
    {
        var root = canvas?.rootCanvas;
        var camera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        rect.GetWorldCorners(Corners);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < 4; i++)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, Corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    internal static bool TooltipOverlaps(Rect bounds)
    {
        foreach (var entry in Windows)
        {
            if (!entry.Tooltip || entry.Window == null) continue;
            var window = entry.Window;
            var panel = window.TryCast<TooltipWindow>()?.currentPanel;
            if (panel == null || !panel.gameObject.activeInHierarchy || !window.gameObject.activeInHierarchy
                || (window._canvas != null && !window._canvas.isActiveAndEnabled)) continue;
            var rect = panel.GetComponent<RectTransform>();
            if (rect != null && bounds.Overlaps(ScreenBounds(rect, panel.GetComponentInParent<Canvas>()))) return true;
        }
        return false;
    }
}
