using System.Security.Cryptography;
using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.Security;
using BackupGuard.Core.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackupGuard.Tests;

/// <summary>
/// Fluxo completo com 7-Zip real e um Drive falso em memória.
/// Garante a regra mais importante: o original só some se o Drive confirmar o arquivo íntegro.
/// </summary>
public sealed class BackupEngineTests : IDisposable
{
    private const string SevenZip = @"C:\Program Files\7-Zip\7z.exe";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bg-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly AppPaths _paths;

    public BackupEngineTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        _paths.EnsureDirectories();
        _source = Path.Combine(_root, "origem");
        Directory.CreateDirectory(_source);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeDrive : IDriveClient
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public readonly List<string> Trashed = [];
        public bool CorruptUpload;

        public Task<string> EnsureFolderAsync(string name, string? parentId, string? knownId, CancellationToken ct) =>
            Task.FromResult(knownId ?? $"folder-{name}");

        public async Task<UploadedFile> UploadAsync(string path, string folderId, string name, IDictionary<string, string> properties, CancellationToken ct)
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (CorruptUpload)
                bytes[^1] ^= 0xFF;
            var id = "file-" + Files.Count;
            Files[id] = bytes;
            return new UploadedFile(id, bytes.Length, Convert.ToHexStringLower(MD5.HashData(bytes)));
        }

        public Task<IReadOnlyList<RemoteBackup>> ListBackupsAsync(string folderId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RemoteBackup>>([]);

        public Task TrashAsync(string fileId, CancellationToken ct)
        {
            Trashed.Add(fileId);
            return Task.CompletedTask;
        }

        public Task<DriveQuota> GetQuotaAsync(CancellationToken ct) => Task.FromResult(new DriveQuota(null, 0));
    }

    private sealed class FakeNotifier : INotifier
    {
        public readonly List<string> Messages = [];

        public Task SendAsync(string html, CancellationToken ct = default)
        {
            Messages.Add(html);
            return Task.CompletedTask;
        }
    }

    private (BackupEngine Engine, FakeDrive Drive, FakeNotifier Notifier, StateStore State) Build()
    {
        var config = new AppConfig
        {
            SevenZipPath = SevenZip,
            CompressionLevel = 1,
            Telegram = new TelegramConfig { Enabled = false },
            Jobs = [new BackupJob { Id = "acme", Name = "Acme", SourceFolder = _source }],
        };
        var secrets = new Secrets { ArchivePassword = "senha-de-teste-123456" };
        var drive = new FakeDrive();
        var notifier = new FakeNotifier();
        var state = new StateStore(_paths);
        var provider = new DriveClientProvider(_ => Task.FromResult<IDriveClient>(drive));
        var engine = new BackupEngine(config, _paths, secrets, provider, new DriveFolders(config, state, provider),
            new SevenZipCompressor(config, NullLogger<SevenZipCompressor>.Instance), notifier,
            new HealthPinger(new HttpClient(), config, NullLogger<HealthPinger>.Instance),
            state, new BackupHistory(_paths), TimeProvider.System, NullLogger<BackupEngine>.Instance);
        return (engine, drive, notifier, state);
    }

    private string CreateSourceFile(string name = "base.bak")
    {
        var path = Path.Combine(_source, name);
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("INSERT INTO clientes VALUES (1, 'teste');\n", 20_000)));
        return path;
    }

    [Fact]
    public async Task Uploads_verifies_and_deletes_original_but_keeps_folder()
    {
        if (!File.Exists(SevenZip)) return; // 7-Zip não instalado nesta máquina
        var (engine, drive, notifier, state) = Build();
        var file = CreateSourceFile();
        var originalSize = new FileInfo(file).Length;

        await engine.RunCycleAsync(ignoreStability: true, CancellationToken.None);

        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(_source));
        var uploaded = Assert.Single(drive.Files).Value;
        Assert.True(uploaded.Length < originalSize / 10, "texto repetitivo deveria compactar muito");
        Assert.Empty(Directory.GetFiles(_paths.WorkDir));
        Assert.NotNull(state.Read(s => s.Jobs["acme"].LastSuccessUtc));
        Assert.Contains(notifier.Messages, m => m.Contains("Backup enviado"));
    }

    [Fact]
    public async Task Hash_mismatch_keeps_original_and_trashes_bad_copy()
    {
        if (!File.Exists(SevenZip)) return; // 7-Zip não instalado nesta máquina
        var (engine, drive, notifier, _) = Build();
        drive.CorruptUpload = true;
        var file = CreateSourceFile();

        await engine.RunCycleAsync(ignoreStability: true, CancellationToken.None);

        Assert.True(File.Exists(file));
        Assert.Single(drive.Trashed);
        Assert.Contains(notifier.Messages, m => m.Contains("Falha no backup"));
        Assert.Empty(Directory.GetFiles(_paths.WorkDir));
    }

    [Fact]
    public async Task Archive_requires_password()
    {
        if (!File.Exists(SevenZip)) return; // 7-Zip não instalado nesta máquina
        var config = new AppConfig { SevenZipPath = SevenZip, CompressionLevel = 1 };
        var zip = new SevenZipCompressor(config, NullLogger<SevenZipCompressor>.Instance);
        var file = CreateSourceFile();
        var archive = Path.Combine(_root, "t.7z");

        await zip.CompressAsync(file, archive, "senha-correta-123456", CancellationToken.None);
        await zip.TestAsync(archive, "senha-correta-123456", CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => zip.TestAsync(archive, "senha-errada", CancellationToken.None));
    }
}
