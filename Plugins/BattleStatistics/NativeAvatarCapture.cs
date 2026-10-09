using GameRiver.Client;
using GameRiver;
using UnityEngine;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal static class NativeAvatarCapture
{
    private static readonly Dictionary<string, float> RetryAfter = new();
    internal static void Capture(string source, PlayerAvatars avatars)
    {
        if (string.IsNullOrWhiteSpace(source) || avatars.Ready(source) is not null) return;
        var remote = Uri.TryCreate(source, UriKind.Absolute, out var uri);
        if (remote && uri!.Host.Equals("avatars.steamstatic.com", StringComparison.OrdinalIgnoreCase)) return;
        if (RetryAfter.GetValueOrDefault(source) > Time.unscaledTime) return;
        RetryAfter[source] = Time.unscaledTime + 10;
        RenderTexture? render = null;
        Texture2D? copy = null;
        var previous = RenderTexture.active;
        try
        {
            var manager = GRUIManager.Instance.GetSpriteManager();
            // Preset avatars can be remote resources already loaded by the game.
            // Capture that image in memory instead of treating every URL as Steam.
            var sprite = remote ? manager.GetDynamicSprite(source) : manager.GetSprite(source);
            if (sprite == null)
            {
                var image = Resources.FindObjectsOfTypeAll<GRImage>()
                    .FirstOrDefault(i => i != null && i.gameObject.activeInHierarchy && i.avatar == source);
                sprite = image?.avatarObj?.GetComponent<UnityEngine.UI.Image>()?.sprite;
            }
            if (sprite == null)
            { BattleStatisticsPlugin.Logger.LogWarning("Selected avatar is not loaded yet: " + source); return; }
            var texture = sprite.texture;
            var region = sprite.textureRect;
            render = RenderTexture.GetTemporary(texture.width, texture.height, 0);
            Graphics.Blit(texture, render);
            RenderTexture.active = render;
            copy = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(region, 0, 0);
            copy.Apply();
            var bytes = ImageConversion.EncodeToPNG(copy);
            if (bytes is not null) avatars.Put(new SavedAvatar(source, "image/png", Convert.ToBase64String(bytes)));
        }
        catch (Exception error) { BattleStatisticsPlugin.Logger.LogWarning("In-game avatar capture: " + error.Message); }
        finally
        {
            RenderTexture.active = previous;
            if (render != null) RenderTexture.ReleaseTemporary(render);
            if (copy != null) UnityEngine.Object.Destroy(copy);
        }
    }
}
