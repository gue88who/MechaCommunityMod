using GameRiver;
using GameRiver.Client;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Reuse the installed game's art. No copied textures or fonts are bundled.
internal sealed class ResultsSkin : IDisposable
{
    private readonly Dictionary<string, Sprite> _sprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Sprite> _portraits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _avatars = new(StringComparer.Ordinal);
    private readonly NativeLevelRing _levelRing = new();
    public void Dispose() { ClearAvatars(); _levelRing.Dispose(); }
    internal void ClearAvatars()
    {
        foreach (var texture in _avatars.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
        _avatars.Clear();
    }
    private bool _loaded;
    private Sprite? _attackIcon, _hpIcon;
    private GUIStyle? _levelText, _levelHint;
    internal Font? Body { get; private set; }
    internal Font? Title { get; private set; }

    internal void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var sprite in sprites)
            if (sprite != null && Wanted(sprite.name)) _sprites.TryAdd(sprite.name, sprite);
        var attackNames = new[] { "icon_attribute_attack", "icon_atk", "atk_icon", "ATK", "icon_attack", "attack_icon", "icon_damage" };
        _attackIcon = attackNames.Select(name => sprites.FirstOrDefault(s => s != null
            && string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(s => s != null)
            ?? sprites.Where(s => s != null && System.Text.RegularExpressions.Regex.IsMatch(s.name,
                @"^(?:ui[_-])?(?:icon[_-])?atk(?:[_-](?:icon|\d+))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(s => s.name.Length).FirstOrDefault();
        BattleStatisticsPlugin.Logger.LogInfo("Regular attack icon: " + (_attackIcon?.name ?? "ATK sprite not loaded"));
        var hpNames = new[] { "icon_attribute_life" };
        _hpIcon = hpNames.Select(name => sprites.FirstOrDefault(s => s != null
            && string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(s => s != null)
            ?? sprites.Where(s => s != null && System.Text.RegularExpressions.Regex.IsMatch(s.name,
                @"^(?:ui[_-])?(?:icon[_-])?hp(?:[_-](?:icon|\d+))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(s => s.name.Length).FirstOrDefault();
        BattleStatisticsPlugin.Logger.LogInfo("Tanking icon: " + (_hpIcon?.name ?? "HP sprite not loaded"));
        var fonts = Resources.FindObjectsOfTypeAll<Font>().Where(f => f != null).ToArray();
        Body = FindFont(fonts, "RobotoCondensed-Regular", "Akrobat-SemiBold", "Akrobat-Regular");
        Title = FindFont(fonts, "Xolonium-Bold", "orbitron-bold", "orbitron-medium") ?? Body;
        BattleStatisticsPlugin.Logger.LogInfo($"Results skin: {_sprites.Count} game sprites, body={Body?.name}, title={Title?.name}");
    }

    private static bool Wanted(string name) => name.StartsWith("button_", StringComparison.Ordinal)
        || name == "replayUI_frame_2" || name is "icon_eye" or "icon_exp" or "Icon_Count"
            or "icon_player_exp_basic" or "icon_player_exp_advance" or "icon_player_exp_bg"
        || name.StartsWith("panel_blue_", StringComparison.Ordinal);

    private static Font? FindFont(Font[] fonts, params string[] names)
        => names.Select(name => fonts.FirstOrDefault(f => string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(f => f != null);

    internal bool Art(Rect rect, string name, Color? tint = null)
    {
        if (!_sprites.TryGetValue(name, out var sprite) || sprite == null) return false;
        if (!Supported(sprite)) return false;
        DrawSprite(rect, sprite, sliced: true, tint: tint);
        return true;
    }
    internal Sprite? GameSprite(string name) => _sprites.GetValueOrDefault(name);

    internal bool AttributedUnitsButton(Rect rect, bool expanded, GUIStyle style)
    {
        var hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Art(rect, "button_" + (expanded ? "y" : "b") + "_x" + (hover ? "2" : "1"));
        var clicked = GUI.Button(rect, "", style);
        Art(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8), "Icon_Count");
        return clicked;
    }

    internal void LevelBadge(Rect rect, UnitLevelBadge badge)
    {
        Art(rect, "icon_player_exp_bg");
        Art(rect, "icon_player_exp_basic", new Color(.55f, .58f, .63f));
        if (badge.Progress is { } progress && _sprites.TryGetValue("icon_player_exp_advance", out var sprite)
            && sprite != null && Supported(sprite))
        {
            if (_levelRing.Get(sprite, progress) is { } ring) GUI.DrawTexture(rect, ring);
        }
        _levelText ??= new GUIStyle { font = Body, alignment = TextAnchor.MiddleCenter };
        _levelText.fontSize = badge.Label.Length > 1 ? 12 : 16;
        _levelText.normal.textColor = Color.white;
        GUI.Label(rect, badge.Label, _levelText);
    }

    internal void LevelHint(Vector2 pointer, Rect bounds, string text)
    {
        _levelHint ??= new GUIStyle { font = Body, fontSize = 14, alignment = TextAnchor.UpperLeft, wordWrap = false };
        _levelHint.normal.textColor = new Color(.74f, .8f, .9f);
        var height = Math.Min(bounds.height - 24, 18 * (text.Count(c => c == '\n') + 1) + 20);
        var rect = new Rect(Math.Clamp(pointer.x + 12, bounds.x + 8, bounds.xMax - 284),
            Math.Clamp(pointer.y + 12, bounds.y + 8, bounds.yMax - height - 8), 276, height);
        var before = GUI.color;
        GUI.color = new Color(.06f, .08f, .12f, 1); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = before;
        Art(rect, "panel_blue_0_with_corners");
        GUI.Label(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20), text, _levelHint);
    }

    internal void Portrait(Rect rect, UnitDisplay display)
    {
        if (string.IsNullOrWhiteSpace(display.Portrait)) return;
        if (!_portraits.TryGetValue(display.Portrait, out var sprite) || sprite == null)
        {
            sprite = display.DirectPortrait ? GRUIManager.Instance.GetSpriteManager().GetSprite(display.Portrait)
                : GRUIManager.Instance.GetSpriteManager().GetSprite(display.Portrait, SpriteType.Mech, (int)CardLevel.Level1);
            if (sprite == null) return;
            _portraits[display.Portrait] = sprite;
        }
        DrawSprite(rect, sprite, sliced: false);
    }

    internal void StatIcon(Rect rect, bool attack)
    {
        var sprite = attack ? _attackIcon : _hpIcon;
        if (sprite != null) DrawSprite(rect, sprite, false);
    }
    internal void PlayerPortrait(Rect rect, string name, SavedAvatar? avatar)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (avatar is not null)
        {
            if (!_avatars.TryGetValue(name, out var texture))
            {
                texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(avatar.Base64), true))
                { UnityEngine.Object.Destroy(texture); return; }
                _avatars[name] = texture;
            }
            GUI.DrawTexture(rect, texture);
            return;
        }
        var key = "player:" + name;
        if (!_portraits.TryGetValue(key, out var sprite) || sprite == null)
        {
            var manager = GRUIManager.Instance.GetSpriteManager();
            // Steam portraits are downloaded URLs, not files in the portrait catalog.
            // Reuse the avatar already loaded by the game's player UI.
            var remote = Uri.TryCreate(name, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
            sprite = remote ? manager.GetDynamicSprite(name)
                : manager.GetSprite(name, SpriteType.PlayerPortrait, 0);
            if (sprite == null) return;
            _portraits[key] = sprite;
        }
        DrawSprite(rect, sprite, false);
    }

    internal void SourcePortrait(Rect rect, DamageSource source)
    {
        if (source.Category == DamageCategory.Attack)
        {
            if (_attackIcon != null) DrawSprite(rect, _attackIcon, false);
            return;
        }
        if (source.Category is DamageCategory.Tech or DamageCategory.Spell)
        {
            if (EffectCatalog.Portrait(source.Category, source.Id) is { } native) DrawSprite(rect, native, false);
            return;
        }
        var icon = source.Category switch {
            DamageCategory.Tech => Config.Instance.GetTechnologyByID(source.Id)?.GetIconName(),
            DamageCategory.Spell => Config.Instance.GetCommanderSkill(source.Id)?.GetIconName(),
            DamageCategory.Other => DeviceIcon(source), _ => null };
        if (string.IsNullOrWhiteSpace(icon)) return;
        var cacheKey = source.Category + ":" + icon;
        if (!_portraits.TryGetValue(cacheKey, out var sprite) || sprite == null)
        {
            var manager = GRUIManager.Instance.GetSpriteManager();
            sprite = source.Category == DamageCategory.Other ? manager.GetSprite(icon)
                : manager.GetSprite(icon, source.Category == DamageCategory.Tech ? SpriteType.Technology : SpriteType.CommanderSkill, 0);
            if (sprite == null) return;
            _portraits[cacheKey] = sprite;
        }
        DrawSprite(rect, sprite, false);
    }

    private static string? DeviceIcon(DamageSource source)
    {
        if (source.Id <= 0) return null;
        // Other effects span multiple catalogs; IDs alone cannot identify an icon.
        var device = Config.Instance.GetContraptionData(source.Id);
        if (device?.GetName() == source.Name) return device.GetIconName();
        var equipment = Config.Instance.GetEquipmentData(source.Id);
        if (equipment?.GetName() == source.Name) return equipment.GetIconName();
        var construction = Config.Instance.GetConstructionData(source.Id);
        return construction?.GetName() == source.Name ? construction.GetIconName() : null;
    }

    private static void DrawSprite(Rect rect, Sprite sprite, bool sliced, Color? tint = null)
    {
        // Keep the same group/control sequence on layout, input and repaint.
        // UI sprites use rectangular, unrotated atlas entries. Do not draw a wrong
        // atlas region if a future game update switches to tight/rotated packing.
        if (!Supported(sprite)) return;
        var texture = sprite.texture;
        var source = sprite.textureRect;
        var b = sliced ? sprite.border : Vector4.zero;
        if (sliced && b == Vector4.zero && sprite.name.StartsWith("button_", StringComparison.Ordinal))
            b = new Vector4(20, 0, 20, 0);
        if (b == Vector4.zero)
        {
            Crop(rect, texture, source, tint);
            return;
        }
        // Preserve authored corners; shrink them proportionally on small buttons.
        var factor = Math.Min(1f, Math.Min(rect.width / Math.Max(1, b.x + b.z), rect.height / Math.Max(1, b.y + b.w)));
        if (b.y + b.w == 0) factor = Math.Min(factor, rect.height / source.height);
        var dx = new[] { rect.x, rect.x + b.x * factor, rect.xMax - b.z * factor, rect.xMax };
        var dy = new[] { rect.y, rect.y + b.w * factor, rect.yMax - b.y * factor, rect.yMax };
        // Slice coordinates remain local to the current GUI group. Screen-space
        // conversion changes their origin in nested, scaled table/round groups.
        var sx = new[] { source.x, source.x + b.x, source.xMax - b.z, source.xMax };
        var sy = new[] { source.yMax, source.yMax - b.w, source.y + b.y, source.y };
        for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
            {
                var destination = new Rect(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]);
                var patch = new Rect(sx[x], sy[y + 1], sx[x + 1] - sx[x], sy[y] - sy[y + 1]);
                if (destination.width <= 0 || destination.height <= 0 || patch.width <= 0 || patch.height <= 0) continue;
                // Independently clipped atlas slices can leave a dark raster seam
                // at fractional GUI scales. Overlap only internal joins by half
                // a physical pixel, extending UVs by the matching amount. The
                // native artwork and outer button silhouette stay unchanged.
                var overlapX = 0.5f / Math.Max(0.01f, Math.Abs(GUI.matrix.m00));
                var overlapY = 0.5f / Math.Max(0.01f, Math.Abs(GUI.matrix.m11));
                var left = x > 0 ? overlapX : 0; var right = x < 2 ? overlapX : 0;
                var top = y > 0 ? overlapY : 0; var bottom = y < 2 ? overlapY : 0;
                var sourceScaleX = patch.width / destination.width;
                var sourceScaleY = patch.height / destination.height;
                patch = new Rect(patch.x - left * sourceScaleX, patch.y - bottom * sourceScaleY,
                    patch.width + (left + right) * sourceScaleX, patch.height + (top + bottom) * sourceScaleY);
                destination = new Rect(destination.x - left, destination.y - top,
                    destination.width + left + right, destination.height + top + bottom);
                Crop(destination, texture, patch, tint);
            }
    }

    private static bool Supported(Sprite sprite) => !sprite.packed ||
        (sprite.packingMode != SpritePackingMode.Tight && sprite.packingRotation == SpritePackingRotation.None);

    private static void Crop(Rect destination, Texture texture, Rect source, Color? tint = null)
    {
        if (destination.width <= 0 || destination.height <= 0 || source.width <= 0 || source.height <= 0) return;
        // DrawTextureWithTexCoords is stripped from this player. Clipping a scaled
        // atlas uses the same available primitives as the working scroll panels.
        var xScale = destination.width / source.width;
        var yScale = destination.height / source.height;
        var before = GUI.color;
        GUI.BeginGroup(destination);
        try
        {
            GUI.color = tint ?? Color.white;
            GUI.DrawTexture(new Rect(-source.x * xScale, -(texture.height - source.yMax) * yScale,
                texture.width * xScale, texture.height * yScale), texture);
        }
        finally { GUI.color = before; GUI.EndGroup(); }
    }
}
