using System.Diagnostics;
using System.Security.Cryptography;
using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Errors;
using Google;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Security;
using BackupGuard.Core.State;
using Microsoft.Extensions.Logging;

namespace BackupGuard.Core.Pipeline;

/// <summary>
/// Coração do serviço: varre as pastas, e para cada backup pronto faz
/// compactar + criptografar → testar → enviar → conferir hash → apagar local.
/// O original só é apagado depois que o Drive confirma o MD5 idêntico.
/// </summary>
public sealed class BackupEngine(
    AppConfig config,
    AppPaths paths,
    Secrets secrets,
    IDriveClientProvider driveProvider,
    DriveFolders folders,
    SevenZipCompressor zip,
    INotifier notifier,
    HealthPinger health,
    StateStore state,
    BackupHistory history,
    TimeProvider time,
    ILogger<BackupEngine> logger)
{
    private sealed class Failure
    {
        public int Count;
        public DateTimeOffset NextAttempt;
        public DateTimeOffset LastAlert;
    }

    private static readonly TimeSpan RealertInterval = TimeSpan.FromHours(6);

    private readonly FileStabilityTracker _tracker = new();
    private readonly Dictionary<string, Failure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _missingFolderAlerts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Remove compactações pela metade deixadas por uma queda de energia ou reinício.</summary>
    public void CleanupWorkDir()
    {
        foreach (var file in Directory.EnumerateFiles(paths.WorkDir))
            TryDelete(file);
        // Andamento de antes de uma queda/reinício não vale mais.
        state.Update(s =>
        {
            foreach (var js in s.Jobs.Values)
                (js.InProgressFile, js.InProgressStage, js.InProgressSinceUtc) = (null, null, null);
        });
    }

    private readonly SemaphoreSlim _cycleGate = new(1, 1);

    /// <summary>
    /// Espera a varredura em andamento terminar e impede novas enquanto o retorno não for descartado.
    /// Usado pela atualização automática, que nunca interrompe um backup no meio.
    /// </summary>
    public async Task<IDisposable> PauseAsync(CancellationToken ct)
    {
        await _cycleGate.WaitAsync(ct);
        return new Releaser(_cycleGate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }

    /// <summary>Uma varredura completa. <paramref name="ignoreStability"/> é usado no teste manual (run-once).</summary>
    public async Task RunCycleAsync(bool ignoreStability, CancellationToken ct)
    {
        using var _ = await PauseAsync(ct);
        await RetryPendingDeletesAsync(ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in config.Jobs)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in await ListReadyFilesAsync(job, ignoreStability, seen, ct))
            {
                ct.ThrowIfCancellationRequested();
                await ProcessFileAsync(job, file, ct);
            }
        }
        _tracker.Prune(seen);
    }

    private async Task<List<FileInfo>> ListReadyFilesAsync(
        BackupJob job, bool ignoreStability, HashSet<string> seen, CancellationToken ct)
    {
        var ready = new List<FileInfo>();
        if (!Directory.Exists(job.SourceFolder))
        {
            var now = time.GetUtcNow();
            if (!_missingFolderAlerts.TryGetValue(job.Id, out var last) || now - last >= RealertInterval)
            {
                _missingFolderAlerts[job.Id] = now;
                logger.LogWarning("Pasta de origem de {Job} não existe: {Folder}", job.Name, job.SourceFolder);
                await notifier.SendAsync(
                    $"⚠️ <b>{Format.Html(job.Name)}</b>: pasta de backup não encontrada\n<code>{Format.Html(job.SourceFolder)}</code>", ct);
            }
            return ready;
        }
        _missingFolderAlerts.Remove(job.Id);

        var option = job.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var patterns = job.FilePatterns.Count == 0 ? ["*"] : job.FilePatterns;
        var files = patterns
            .SelectMany(p => SafeEnumerate(job.SourceFolder, p, option))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !ArchiveNaming.IsIgnored(p));

        var pending = state.Read(s => s.PendingDeletes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        foreach (var path in files)
        {
            seen.Add(path);
            if (pending.Contains(path))
                continue;
            if (_failures.TryGetValue(path, out var failure) && time.GetUtcNow() < failure.NextAttempt)
                continue;

            var info = new FileInfo(path);
            if (!info.Exists)
                continue;
            var stable = ignoreStability || _tracker.IsStable(
                path, info.Length, info.LastWriteTimeUtc, time.GetUtcNow(), TimeSpan.FromSeconds(job.StableSeconds));
            if (stable && !FileStabilityTracker.IsLocked(path))
                ready.Add(info);
        }
        return ready.OrderBy(f => f.LastWriteTimeUtc).ToList();
    }

    private IEnumerable<string> SafeEnumerate(string folder, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern,
                new EnumerationOptions { RecurseSubdirectories = option == SearchOption.AllDirectories, IgnoreInaccessible = true })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro ao listar {Folder}", folder);
            return [];
        }
    }

    private async Task ProcessFileAsync(BackupJob job, FileInfo source, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var archiveName = ArchiveNaming.For(job.Id, source.FullName, source.LastWriteTime);
        var archivePath = Path.Combine(paths.WorkDir, archiveName);
        var sourceSize = source.Length;
        var sourceWrite = source.LastWriteTimeUtc;
        logger.LogInformation("Iniciando backup de {File} ({Size})", source.FullName, Format.Bytes(sourceSize));
        SetProgress(job, Path.GetFileName(source.FullName), "Compactando");

        try
        {
            if (string.IsNullOrEmpty(secrets.ArchivePassword))
                throw new InvalidOperationException("Senha dos arquivos não configurada. Rode 'BackupGuard.Service secret password'.");
            EnsureFreeSpace(sourceSize);

            var compressTimer = Stopwatch.StartNew();
            var compression = await zip.CompressAsync(source.FullName, archivePath, secrets.ArchivePassword, ct);
            SetProgress(job, Path.GetFileName(source.FullName), "Conferindo");
            await zip.TestAsync(archivePath, secrets.ArchivePassword, ct);
            compressTimer.Stop();
            var archiveSize = new FileInfo(archivePath).Length;
            var localMd5 = await Md5Async(archivePath, ct);

            SetProgress(job, Path.GetFileName(source.FullName), "Enviando");
            var uploadTimer = Stopwatch.StartNew();
            var folderId = await folders.ForJobAsync(job, ct);
            var drive = await driveProvider.GetAsync(ct);
            var uploaded = await drive.UploadAsync(archivePath, folderId, archiveName, new Dictionary<string, string>
            {
                ["job"] = job.Id,
                ["source"] = Path.GetFileName(source.FullName),
                ["sourceSize"] = sourceSize.ToString(),
                ["machine"] = config.MachineName,
            }, ct);
            uploadTimer.Stop();

            if (!string.Equals(uploaded.Md5, localMd5, StringComparison.OrdinalIgnoreCase) || uploaded.Size != archiveSize)
            {
                // Arquivo chegou diferente: apaga a cópia ruim do Drive e mantém o original.
                await TryTrashAsync(drive, uploaded.Id, ct);
                throw new IOException(
                    $"Conferência falhou: MD5 local {localMd5} ≠ Drive {uploaded.Md5} (tamanho {archiveSize} vs {uploaded.Size}).");
            }

            TryDelete(archivePath);
            var warnings = DetectAnomalies(job, sourceSize, archiveSize);
            var sourceDeleted = await DeleteSourceAsync(source.FullName);
            if (!sourceDeleted)
                state.Update(s => s.PendingDeletes[source.FullName] =
                    new UploadedSource(source.FullName, sourceSize, sourceWrite, uploaded.Id));

            state.Update(s =>
            {
                var cs = state.Job(s, job.Id);
                cs.LastSuccessUtc = time.GetUtcNow();
                cs.LastArchiveName = archiveName;
                cs.LastSourceSize = sourceSize;
                cs.LastArchiveSize = archiveSize;
            });
            history.Append(new HistoryEntry(time.GetUtcNow(), job.Id, source.FullName, true, sourceSize,
                archiveSize, archiveName, uploaded.Id, started.Elapsed.TotalSeconds, null));
            _failures.Remove(source.FullName);
            _tracker.Forget(source.FullName);

            logger.LogInformation("Backup concluído: {Archive} ({Size}) em {Elapsed}",
                archiveName, Format.Bytes(archiveSize), started.Elapsed);

            var msg = $"✅ <b>Backup enviado — {Format.Html(job.Name)}</b>\n" +
                      $"📄 {Format.Html(Path.GetFileName(source.FullName))}\n" +
                      $"📦 {Format.Bytes(sourceSize)} → {Format.Bytes(archiveSize)} ({Format.Percent((double)archiveSize / Math.Max(sourceSize, 1))} do original)\n" +
                      $"⏱ compactação {Format.Duration(compressTimer.Elapsed)} · envio {Format.Duration(uploadTimer.Elapsed)}\n" +
                      $"⚙️ {compression.Description} · {Format.Bytes((long)(sourceSize / Math.Max(compressTimer.Elapsed.TotalSeconds, 1)))}/s\n" +
                      $"🔐 hash conferido no Drive";
            if (!sourceDeleted)
                msg += "\n⚠️ não foi possível apagar o arquivo local; nova tentativa automática.";
            foreach (var w in warnings)
                msg += "\n" + w;
            await notifier.SendAsync(msg, ct);
            await health.SuccessAsync($"{job.Id}: {archiveName}", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDelete(archivePath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(archivePath);
            await RegisterFailureAsync(job, source.FullName, sourceSize, started.Elapsed, ex, ct);
        }
        finally
        {
            SetProgress(job, null, null);
        }
    }

    /// <summary>Registra o que está sendo feito agora (Painel e alerta de "backup não chegou").</summary>
    private void SetProgress(BackupJob job, string? file, string? stage) => state.Update(s =>
    {
        var js = state.Job(s, job.Id);
        if (file is not null && js.InProgressFile != file)
            js.InProgressSinceUtc = time.GetUtcNow();
        js.InProgressFile = file;
        js.InProgressStage = stage;
        if (file is null)
            js.InProgressSinceUtc = null;
    });

    /// <summary>
    /// Sinais de problema: o backup encolheu muito (corrompido/truncado) ou quase não
    /// compactou quando antes compactava bem (pode ter sido criptografado por ransomware).
    /// </summary>
    internal List<string> DetectAnomalies(BackupJob job, long sourceSize, long archiveSize)
    {
        var warnings = new List<string>();
        var previous = state.Read(s => s.Jobs.GetValueOrDefault(job.Id));
        if (previous?.LastSourceSize is > 0 and var lastSize && sourceSize < lastSize * 0.5)
            warnings.Add($"⚠️ <b>Atenção:</b> backup {Format.Percent(1 - (double)sourceSize / lastSize)} menor que o anterior " +
                         $"({Format.Bytes(lastSize)} → {Format.Bytes(sourceSize)}). Verifique se o programa que gera o backup funcionou corretamente.");

        if (previous?.LastSourceSize is > 0 && previous.LastArchiveSize is > 0 && sourceSize > 1_000_000)
        {
            var lastRatio = (double)previous.LastArchiveSize.Value / previous.LastSourceSize.Value;
            var ratio = (double)archiveSize / sourceSize;
            if (lastRatio < 0.5 && ratio > 0.9)
                warnings.Add("🚨 <b>ALERTA:</b> o arquivo quase não compactou, ao contrário dos anteriores. " +
                             "Isso acontece quando o arquivo está criptografado (ransomware) ou corrompido. Verifique o servidor!");
        }
        return warnings;
    }

    private async Task RegisterFailureAsync(
        BackupJob job, string path, long size, TimeSpan elapsed, Exception ex, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!_failures.TryGetValue(path, out var failure))
            _failures[path] = failure = new Failure();
        failure.Count++;
        // Espera crescente entre tentativas: 1, 2, 4, 8... até 60 minutos.
        failure.NextAttempt = now + TimeSpan.FromMinutes(Math.Min(Math.Pow(2, failure.Count - 1), 60));

        var error = FriendlyError.From(ex);
        if (FriendlyError.RootCause(ex) is GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound })
            folders.Forget(job.Id); // pasta apagada no Drive: será recriada na próxima tentativa

        logger.LogError(ex, "Falha no backup de {File} (tentativa {Count}): {Technical}", path, failure.Count, error.Technical);
        history.Append(new HistoryEntry(now, job.Id, path, false, size, null, null, null, elapsed.TotalSeconds, error.Text));

        if (failure.Count == 1 || now - failure.LastAlert >= RealertInterval)
        {
            failure.LastAlert = now;
            var msg = $"❌ <b>Falha no backup — {Format.Html(job.Name)}</b>\n" +
                      $"📄 {Format.Html(Path.GetFileName(path))}\n\n" +
                      $"{Format.Html(error.Message)}\n";
            if (error.Action is not null)
                msg += $"👉 {Format.Html(error.Action)}\n";
            msg += $"\n🔁 tentativa {failure.Count}; próxima às {failure.NextAttempt.ToLocalTime():HH:mm}. O arquivo original foi mantido.\n" +
                   $"<i>Detalhe técnico: {Format.Html(Truncate(error.Technical, 200))}</i>";
            await notifier.SendAsync(msg, ct);
            await health.FailAsync($"{job.Id}: {error.Technical}", ct);
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private async Task RetryPendingDeletesAsync(CancellationToken ct)
    {
        var pending = state.Read(s => s.PendingDeletes.Values.ToList());
        foreach (var item in pending)
        {
            var info = new FileInfo(item.Path);
            // Se o arquivo mudou desde o envio, é um backup novo com o mesmo nome: volta para a fila normal.
            var gone = !info.Exists || info.Length != item.Size || info.LastWriteTimeUtc != item.LastWriteUtc;
            if (gone || await DeleteSourceAsync(item.Path))
            {
                state.Update(s => s.PendingDeletes.Remove(item.Path));
                if (!gone)
                    await notifier.SendAsync($"🗑 Arquivo local apagado após nova tentativa: {Format.Html(Path.GetFileName(item.Path))}", ct);
            }
        }
    }

    private async Task<bool> DeleteSourceAsync(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Não foi possível apagar {File} (tentativa {Attempt}): {Message}", path, attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
        return false;
    }

    private void EnsureFreeSpace(long sourceSize)
    {
        var drive = new DriveInfo(Path.GetPathRoot(paths.WorkDir)!);
        // Pior caso: arquivo incompressível do mesmo tamanho do original, mais folga.
        var needed = sourceSize + 512L * 1024 * 1024;
        if (drive.AvailableFreeSpace < needed)
            throw new IOException(
                $"Espaço insuficiente em {drive.Name} para compactar: livre {Format.Bytes(drive.AvailableFreeSpace)}, necessário ~{Format.Bytes(needed)}.");
    }

    private async Task TryTrashAsync(IDriveClient drive, string fileId, CancellationToken ct)
    {
        try
        {
            await drive.TrashAsync(fileId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível remover do Drive o arquivo com hash divergente {Id}", fileId);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Não foi possível apagar o temporário {File}: {Message}", path, ex.Message);
        }
    }

    private static async Task<string> Md5Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, ct));
    }
}
