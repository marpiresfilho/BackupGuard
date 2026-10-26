using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BackupGuard.Core.Config;
using BackupGuard.Core.Updates;

namespace BackupGuard.Tests;

/// <summary>Garante que só versões assinadas, mais novas e íntegras são aceitas.</summary>
public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bg-upd-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly (byte[] Private, string Public) _key = UpdateSignature.CreateKeyPair();

    public UpdateTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureDirectories();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Simula a API e os downloads do GitHub.</summary>
    private sealed class FakeGitHub(Dictionary<string, byte[]> files, string tag) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("/releases/latest"))
            {
                var assets = string.Join(",", files.Keys.Select(n => $$"""{"name":"{{n}}","browser_download_url":"https://dl/{{n}}"}"""));
                var json = $$"""{"tag_name":"{{tag}}","body":"notas","assets":[{{assets}}]}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            }
            var name = url["https://dl/".Length..];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(files[name]) });
        }
    }

    private static byte[] MakeZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in UpdateManager.RequiredFiles)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                    w.Write("exe");
        return ms.ToArray();
    }

    private UpdateManager Build(string version, string tag, Action<Dictionary<string, byte[]>>? tamper = null, string? signedWith = null)
    {
        var zip = MakeZip();
        var manifest = new UpdateManifest(version, "pkg.zip", Convert.ToHexStringLower(SHA256.HashData(zip)), "notas", DateTimeOffset.UtcNow);
        var bytes = manifest.ToBytes();
        var files = new Dictionary<string, byte[]>
        {
            ["pkg.zip"] = zip,
            [UpdateManifest.FileName] = bytes,
            [UpdateManifest.SignatureFileName] = UpdateSignature.Sign(bytes, _key.Private),
        };
        tamper?.Invoke(files);
        var github = new GitHubReleases(new HttpClient(new FakeGitHub(files, tag)), "dono/repo");
        return new UpdateManager(_paths, github, signedWith ?? _key.Public);
    }

    [Fact]
    public async Task Accepts_newer_signed_version_and_stages_it()
    {
        var manager = Build("0.3.0", "v0.3.0");
        var update = await manager.CheckAsync("0.2.0", CancellationToken.None);
        Assert.NotNull(update);
        var staged = await manager.StageAsync(update, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(staged, "BackupGuard.Service.exe")));
    }

    [Fact]
    public async Task Ignores_same_or_older_version()
    {
        Assert.Null(await Build("0.2.0", "v0.2.0").CheckAsync("0.2.0", CancellationToken.None));
        Assert.Null(await Build("0.1.9", "v0.1.9").CheckAsync("0.2.0", CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_signature_from_another_key()
    {
        var other = UpdateSignature.CreateKeyPair().PublicKeyBase64;
        var manager = Build("0.3.0", "v0.3.0", signedWith: other);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.CheckAsync("0.2.0", CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_altered_manifest()
    {
        var manager = Build("0.3.0", "v0.3.0", files =>
            files[UpdateManifest.FileName] = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(files[UpdateManifest.FileName]).Replace("0.3.0", "9.9.9")));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.CheckAsync("0.2.0", CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_swapped_package()
    {
        var manager = Build("0.3.0", "v0.3.0", files => files["pkg.zip"] = [.. files["pkg.zip"], 0x00]);
        var update = await manager.CheckAsync("0.2.0", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.StageAsync(update!, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_tag_that_differs_from_signed_version()
    {
        var manager = Build("0.3.0", "v0.4.0");
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.CheckAsync("0.2.0", CancellationToken.None));
    }

    [Theory]
    [InlineData("v0.10.0", "0.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0-beta", "0.9.0", true)]
    public void Version_comparison(string candidate, string current, bool newer) =>
        Assert.Equal(newer, AppVersion.IsNewer(candidate, current));
}
