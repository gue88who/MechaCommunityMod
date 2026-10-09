using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Read the game's ring once and retain a bounded cache of angular masks in
// memory. Drawing one texture avoids seams between fractional GUI clip boxes.
internal sealed class NativeLevelRing : IDisposable
{
    private readonly Dictionary<int, Texture2D> _masks = new();
    private readonly Queue<int> _order = new();
    private Color32[]? _pixels;
    private long _source;
    private int _width, _height;
    private float _retryAfter;

    internal Texture2D? Get(Sprite sprite, double progress)
    {
        var key = UnitLevelArc.Key(progress);
        if (key == 0) return null;
        if (_source != sprite.Pointer.ToInt64()) Reset();
        if (_masks.TryGetValue(key, out var cached)) return cached;
        if (Time.unscaledTime < _retryAfter) return null;
        Texture2D? texture = null;
        try
        {
            if (_pixels is null) Capture(sprite);
            var masked = _pixels!.ToArray();
            for (var y = 0; y < _height; y++)
                for (var x = 0; x < _width; x++)
                {
                    var i = y * _width + x;
                    if (masked[i].a == 0) continue;
                    var coverage = UnitLevelArc.Coverage(key / 360d, (x + .5) / _width,
                        1 - (y + .5) / _height, 1d / _width, 1d / _height);
                    masked[i].a = (byte)Math.Round(masked[i].a * coverage);
                }
            texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(new Il2CppStructArray<Color32>(masked));
            texture.Apply(false, true);
            if (_masks.Count >= 96) { var oldest = _order.Dequeue(); Object.Destroy(_masks[oldest]); _masks.Remove(oldest); }
            _masks[key] = texture; _order.Enqueue(key);
            return texture;
        }
        catch (Exception error)
        {
            if (texture != null) Object.Destroy(texture);
            _retryAfter = Time.unscaledTime + 15;
            BattleStatisticsPlugin.Logger.LogWarning("Unit level ring: " + error.Message);
            return null;
        }
    }

    private void Capture(Sprite sprite)
    {
        RenderTexture? render = null;
        Texture2D? copy = null;
        var previous = RenderTexture.active;
        try
        {
            var atlas = sprite.texture;
            var region = sprite.textureRect;
            _width = Math.Clamp((int)region.width, 32, 128); _height = Math.Clamp((int)region.height, 32, 128);
            render = RenderTexture.GetTemporary(_width, _height, 0);
            // Blit only this atlas entry, not the entire UI atlas.
            Graphics.Blit(atlas, render, new Vector2(region.width / atlas.width, region.height / atlas.height),
                new Vector2(region.x / atlas.width, region.y / atlas.height));
            RenderTexture.active = render;
            copy = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
            _pixels = copy.GetPixels32().ToArray();
            _source = sprite.Pointer.ToInt64();
        }
        finally
        {
            RenderTexture.active = previous;
            if (render != null) RenderTexture.ReleaseTemporary(render);
            if (copy != null) Object.Destroy(copy);
        }
    }

    private void Reset()
    {
        foreach (var texture in _masks.Values) if (texture != null) Object.Destroy(texture);
        _masks.Clear(); _order.Clear(); _pixels = null; _source = 0;
    }
    public void Dispose() => Reset();
}
