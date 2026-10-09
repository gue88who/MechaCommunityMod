namespace MechaCommunityMod.Plugins.BattleStatistics;

// Native control bounds in the overlay's reference coordinates, measured from
// the top-left. Compute lateral placement before reserving lower HUD space.
internal readonly record struct HudBounds(float Left, float Top, float Right, float Bottom);
internal readonly record struct HudLane(float Inset, float Bottom);

internal static class SideHudLayout
{
    internal static HudBounds? SelectedUnit(IEnumerable<HudBounds> controls, HudBounds native,
        float screenWidth, float screenHeight, float desiredWidth, float height)
    {
        const float gap = 8;
        height = Math.Min(height, screenHeight - gap * 2);
        if (height <= 0) return null;
        var left = Math.Max(gap, native.Right + gap);
        var top = Math.Clamp(native.Bottom - height, gap, screenHeight - gap - height);
        var right = screenWidth - gap;
        foreach (var r in controls)
        {
            if (r.Right <= r.Left || r.Bottom <= r.Top || r.Right + gap <= left
                || r.Bottom + gap <= top || r.Top - gap >= top + height) continue;
            right = Math.Min(right, r.Left - gap);
        }
        var width = Math.Min(desiredWidth, right - left);
        return width >= 360 ? new HudBounds(left, top, left + width, top + height) : null;
    }

    internal static (HudLane Left, HudLane Right) Arrange(IEnumerable<HudBounds> controls,
        float screenWidth, float top, float bottom, bool spectating, float width,
        IEnumerable<HudBounds>? lowerControls = null)
    {
        var visible = controls.Where(r => r.Bottom > top && r.Top < bottom && r.Right > 0
            && r.Left < screenWidth && r.Right - r.Left <= screenWidth * .5f).ToArray();
        var left = 8f; var right = 8f;
        foreach (var r in visible)
        {
            if (!spectating && r.Top >= top + 118) continue;
            if ((r.Left + r.Right) * .5f < screenWidth * .5f) left = Math.Max(left, r.Right + 8);
            else right = Math.Max(right, screenWidth - r.Left + 8);
        }
        var leftBottom = bottom; var rightBottom = bottom;
        if (!spectating)
            foreach (var r in visible)
            {
                if (r.Top < top + 118) continue;
                if (r.Left < left + width && r.Right > left) leftBottom = Math.Min(leftBottom, r.Top - 8);
                if (r.Left < screenWidth - right && r.Right > screenWidth - right - width)
                    rightBottom = Math.Min(rightBottom, r.Top - 8);
            }
        leftBottom = Math.Max(top + 110, leftBottom);
        rightBottom = Math.Max(top + 110, rightBottom);
        // Selected-unit menus reserve height in both play and spectator modes.
        // Do not force a minimum height through a menu; the view can hide if
        // even one row cannot fit. Only the horizontally overlapping side shrinks.
        foreach (var r in lowerControls ?? Array.Empty<HudBounds>())
        {
            if (r.Bottom <= top || r.Top >= bottom) continue;
            if (r.Left < left + width && r.Right > left) leftBottom = Math.Min(leftBottom, r.Top - 8);
            if (r.Left < screenWidth - right && r.Right > screenWidth - right - width)
                rightBottom = Math.Min(rightBottom, r.Top - 8);
        }
        return (new(left, Math.Max(top, leftBottom)), new(right, Math.Max(top, rightBottom)));
    }
}
