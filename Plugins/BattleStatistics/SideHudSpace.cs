using GameRiver;
using GameRiver.Client;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Read the native side controls rather than assuming that their upper half is
// empty. Spectator/replay unit lists and equipment can extend almost to the top.
internal sealed class SideHudSpace
{
    private static readonly Dictionary<long, Component> Panels = new();
    private static int _revision;
    private bool _bootstrapped;
    private int _seenRevision = -1;
    private (Graphic Graphic, bool LowerHud)[] _graphics = Array.Empty<(Graphic, bool)>();
    private readonly Il2CppStructArray<Vector3> _corners = new(4);
    private float _discoverAt;
    private float _warnAt;
    private static bool _unitsDiscovered;
    private float _measureAt, _measuredScale;
    private int _measuredWidth, _measuredHeight;
    private (HudBounds[] Controls, HudBounds[] LowerControls) _bounds = (Array.Empty<HudBounds>(), Array.Empty<HudBounds>());

    internal static CardInfoPanel? ActiveUnitPanel()
    {
        if (!_unitsDiscovered)
        {
            _unitsDiscovered = true;
            foreach (var panel in Resources.FindObjectsOfTypeAll<CardInfoPanel>())
                if (panel != null) Track(panel);
        }
        foreach (var panel in Panels.Values)
            if (panel != null && panel.TryCast<CardInfoPanel>() is { } card
                && card.gameObject.activeInHierarchy && card.actor != null) return card;
        return null;
    }

    internal static Sprite? AvatarSprite(string source)
    {
        // Init hooks retain the native HUD panels. Search those small hierarchies
        // rather than all loaded images, including assets outside the match.
        foreach (var panel in Panels.Values)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy) continue;
            foreach (var image in panel.GetComponentsInChildren<GRImage>(false))
                if (image != null && image.avatar == source
                    && image.avatarObj?.GetComponent<Image>()?.sprite is { } sprite) return sprite;
        }
        return null;
    }

    internal static void Track(Component panel)
    {
        Panels[panel.Pointer.ToInt64()] = panel;
        _revision++;
    }

    [HarmonyPatch(typeof(PlayerInfoSidePanel), nameof(PlayerInfoSidePanel.Init))]
    private static class SideCreated
    {
        private static void Postfix(PlayerInfoSidePanel __instance) => Track(__instance);
    }

    [HarmonyPatch(typeof(PlayerDetailInfoPanel), nameof(PlayerDetailInfoPanel.Init))]
    private static class DetailCreated
    {
        private static void Postfix(PlayerDetailInfoPanel __instance) => Track(__instance);
    }

    [HarmonyPatch(typeof(CardInfoPanel), nameof(CardInfoPanel.Init))]
    private static class UnitMenuCreated
    {
        private static void Postfix(CardInfoPanel __instance) => Track(__instance);
    }

    internal (float Left, float Right, float LeftBottom, float RightBottom) Layout(
        float top, float bottom, float scale, bool spectating, float width)
    {
        var bounds = ReadBounds(scale);
        var pixelsPerUnit = Screen.height * scale / 1080f;
        var lanes = SideHudLayout.Arrange(bounds.Controls, Screen.width / pixelsPerUnit,
            top, bottom, spectating, width, bounds.LowerControls);
        return (lanes.Left.Inset, lanes.Right.Inset, lanes.Left.Bottom, lanes.Right.Bottom);
    }

    internal (HudBounds[] Controls, HudBounds[] LowerControls) ReadBounds(float scale)
    {
        if (_seenRevision == _revision && Time.unscaledTime < _measureAt
            && scale == _measuredScale && Screen.width == _measuredWidth && Screen.height == _measuredHeight)
            return _bounds;
        using var timing = new SlowOperation("HUD placement");
        try
        {
            if (!_bootstrapped)
            {
                // Only discover pre-existing panels once. Native Init callbacks
                // register later panels, avoiding global Unity scans in combat.
                _bootstrapped = true;
                foreach (var panel in Resources.FindObjectsOfTypeAll<PlayerInfoSidePanel>())
                    if (panel != null) Track(panel);
                foreach (var panel in Resources.FindObjectsOfTypeAll<PlayerDetailInfoPanel>())
                    if (panel != null) Track(panel);
                foreach (var panel in Resources.FindObjectsOfTypeAll<CardInfoPanel>())
                    if (panel != null) Track(panel);
            }
            if (_seenRevision != _revision || Time.unscaledTime >= _discoverAt)
            {
                _seenRevision = _revision;
                _discoverAt = Time.unscaledTime + 1;
                var graphics = new List<(Graphic Graphic, bool LowerHud)>();
                foreach (var key in Panels.Keys.ToArray())
                {
                    var panel = Panels[key];
                    if (panel == null) { Panels.Remove(key); continue; }
                    var lowerHud = panel.TryCast<CardInfoPanel>() != null;
                    graphics.AddRange(panel.GetComponentsInChildren<Graphic>(true).Select(g => (g, lowerHud)));
                }
                _graphics = graphics.GroupBy(g => g.Graphic.Pointer)
                    .Select(g => (g.First().Graphic, g.Any(item => item.LowerHud))).ToArray();
            }

            // The overlay canvas matches screen height; the native HUD can have
            // a different scale and camera. Compare both in screen pixels.
            var pixelsPerUnit = Screen.height * scale / 1080f;
            var controls = new List<HudBounds>();
            var lowerControls = new List<HudBounds>();
            foreach (var (graphic, lowerHud) in _graphics)
            {
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.color.a <= 0
                    || graphic.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                var canvas = graphic.canvas;
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var root = canvas.rootCanvas;
                var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                graphic.rectTransform.GetWorldCorners(_corners);
                var xMin = float.PositiveInfinity;
                var xMax = float.NegativeInfinity;
                var yMin = float.PositiveInfinity;
                var yMax = float.NegativeInfinity;
                for (var i = 0; i < 4; i++)
                {
                    var point = RectTransformUtility.WorldToScreenPoint(camera, _corners[i]);
                    xMin = Math.Min(xMin, point.x);
                    xMax = Math.Max(xMax, point.x);
                    yMin = Math.Min(yMin, point.y);
                    yMax = Math.Max(yMax, point.y);
                }
                (lowerHud ? lowerControls : controls).Add(new(xMin / pixelsPerUnit, (Screen.height - yMax) / pixelsPerUnit,
                    xMax / pixelsPerUnit, (Screen.height - yMin) / pixelsPerUnit));
            }
            _measureAt = Time.unscaledTime + .1f;
            _measuredScale = scale; _measuredWidth = Screen.width; _measuredHeight = Screen.height;
            return _bounds = (controls.ToArray(), lowerControls.ToArray());
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _warnAt)
            {
                _warnAt = Time.unscaledTime + 15;
                BattleStatisticsPlugin.Logger.LogWarning("Side HUD placement: " + e.Message);
            }
        }
        return (Array.Empty<HudBounds>(), Array.Empty<HudBounds>());
    }
}
