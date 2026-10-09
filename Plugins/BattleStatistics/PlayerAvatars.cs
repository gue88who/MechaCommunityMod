using System.Net.Http;

namespace MechaCommunityMod.Plugins.BattleStatistics;

internal sealed record SavedAvatar(string Source, string ContentType, string Base64);

// Match-local memory only: no downloaded image files or persistent cache.
internal sealed class PlayerAvatars
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, Task<SavedAvatar?>> _downloads = new(StringComparer.Ordinal);

    internal void Clear() => _downloads.Clear();
    internal void Put(SavedAvatar avatar) => _downloads[avatar.Source] = Task.FromResult<SavedAvatar?>(avatar);
    internal Task<SavedAvatar?> Get(string source)
    {
        if (_downloads.TryGetValue(source, out var cached)) return cached;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("avatars.steamstatic.com", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<SavedAvatar?>(null);
        if (!_downloads.TryGetValue(source, out var task))
            _downloads[source] = task = Download(uri);
        return task;
    }

    internal SavedAvatar? Ready(string source)
    {
        var task = Get(source);
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    private static async Task<SavedAvatar?> Download(Uri uri)
    {
        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/jpeg" or "image/png")) return null;
            const int limit = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > limit) return null;
                output.Write(buffer, 0, count);
            }
            return new(uri.AbsoluteUri, type, Convert.ToBase64String(output.ToArray()));
        }
        catch (Exception) { return null; } // An unavailable avatar must not prevent saving stats.
    }
}
