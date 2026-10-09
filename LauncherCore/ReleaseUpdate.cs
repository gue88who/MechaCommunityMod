using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MechaCommunityMod.Configuration;

internal sealed record ReleaseUpdate(string Tag, string AssetName)
{
    private const string Root = "https://github.com/gue88who/MechaCommunityMod/releases/";
    internal static ReleaseUpdate? FromLocation(Uri? location, string installed)
    {
        if (location is null || !location.IsAbsoluteUri || !location.AbsoluteUri.StartsWith(Root + "tag/", StringComparison.Ordinal)) return null;
        var tag = location.AbsoluteUri[(Root.Length + 4)..];
        var number = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Regex.IsMatch(number, @"^\d+\.\d+\.\d+$") || !Version.TryParse(number, out var version)
            || !Version.TryParse(installed, out var current) || version <= current) return null;
        return new(tag, $"MechaCommunityMod-{number}-Setup.exe");
    }

    internal static async Task<ReleaseUpdate?> Check(string installed, CancellationToken cancellation)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Root + "latest"), cancellation);
        return response.StatusCode == HttpStatusCode.Found ? FromLocation(response.Headers.Location, installed) : null;
    }

    internal static string Checksum(string text, string assetName)
    {
        var match = Regex.Match(text.Trim(), @"^([a-fA-F0-9]{64})\s+\*?([^\r\n]+)$");
        if (!match.Success || match.Groups[2].Value != assetName) throw new InvalidDataException("The update checksum is invalid.");
        return match.Groups[1].Value;
    }

    internal async Task<string> Download(CancellationToken cancellation)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var url = Root + "download/" + Tag + "/" + AssetName;
        var checksum = Checksum(await client.GetStringAsync(url + ".sha256", cancellation), AssetName);
        var directory = Path.Combine(Path.GetTempPath(), "MechaCommunityMod-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AssetName);
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await response.Content.CopyToAsync(output, cancellation);
            await using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
            if (!hash.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update download failed verification. Please try again.");
            return path;
        }
        catch { File.Delete(path); throw; }
    }
}
