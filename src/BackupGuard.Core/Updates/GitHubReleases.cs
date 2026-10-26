using System.Net.Http.Headers;
using System.Text.Json;

namespace BackupGuard.Core.Updates;

public sealed record ReleaseInfo(string Tag, string? Notes, IReadOnlyDictionary<string, string> Assets);

/// <summary>Consulta a última versão publicada nos Releases do GitHub (repositório público, sem login).</summary>
public sealed class GitHubReleases(HttpClient http, string repository)
{
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BackupGuard", AppInfo.Version));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // nenhuma versão publicada ainda
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray().ToDictionary(
            a => a.GetProperty("name").GetString()!,
            a => a.GetProperty("browser_download_url").GetString()!,
            StringComparer.OrdinalIgnoreCase);
        return new ReleaseInfo(
            root.GetProperty("tag_name").GetString()!,
            root.TryGetProperty("body", out var body) ? body.GetString() : null,
            assets);
    }

    public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BackupGuard", AppInfo.Version));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DownloadFileAsync(string url, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BackupGuard", AppInfo.Version));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, ct);
    }
}
