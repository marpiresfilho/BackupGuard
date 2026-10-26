using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using BackupGuard.Core.Config;

namespace BackupGuard.Core.Updates;

public sealed record AvailableUpdate(UpdateManifest Manifest, string ZipUrl)
{
    public string Version => Manifest.Version;
}

/// <summary>Resultado gravado pelo aplicador e lido pelo serviço quando sobe de novo.</summary>
public sealed record UpdateResult(string FromVersion, string ToVersion, bool Success, string? Error, DateTimeOffset TimestampUtc);

/// <summary>
/// Procura, baixa e confere atualizações. Uma versão só é aceita se o release.json estiver
/// assinado pela nossa chave, for mais nova que a atual e o .zip bater com o SHA-256 assinado.
/// </summary>
public sealed class UpdateManager(AppPaths paths, GitHubReleases github, string publicKey = UpdateSignature.PublicKey)
{
    /// <summary>Arquivos que um pacote precisa ter para ser instalado.</summary>
    public static readonly string[] RequiredFiles = ["BackupGuard.Service.exe", "BackupGuard.exe"];

    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, CancellationToken ct)
    {
        var release = await github.GetLatestAsync(ct);
        if (release is null)
            return null;

        if (!release.Assets.TryGetValue(UpdateManifest.FileName, out var manifestUrl)
            || !release.Assets.TryGetValue(UpdateManifest.SignatureFileName, out var signatureUrl))
            throw new InvalidDataException($"A versão {release.Tag} foi publicada sem assinatura e foi ignorada.");

        var manifestBytes = await github.DownloadBytesAsync(manifestUrl, ct);
        var signature = await github.DownloadBytesAsync(signatureUrl, ct);
        if (!UpdateSignature.Verify(manifestBytes, signature, publicKey))
            throw new InvalidDataException($"A assinatura da versão {release.Tag} não confere. Atualização recusada por segurança.");

        var manifest = UpdateManifest.Parse(manifestBytes);
        if (AppVersion.Parse(manifest.Version) != AppVersion.Parse(release.Tag))
            throw new InvalidDataException($"A versão assinada ({manifest.Version}) não corresponde à publicada ({release.Tag}).");
        if (!AppVersion.IsNewer(manifest.Version, currentVersion))
            return null;
        if (!release.Assets.TryGetValue(manifest.File, out var zipUrl))
            throw new InvalidDataException($"O pacote {manifest.File} não está anexado à versão {release.Tag}.");
        return new AvailableUpdate(manifest, zipUrl);
    }

    /// <summary>Baixa, confere o SHA-256 e extrai para uma pasta temporária protegida. Devolve a pasta.</summary>
    public async Task<string> StageAsync(AvailableUpdate update, CancellationToken ct)
    {
        var dir = Path.Combine(paths.UpdatesDir, update.Version);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var zip = Path.Combine(dir, update.Manifest.File);
        await github.DownloadFileAsync(update.ZipUrl, zip, ct);

        string hash;
        await using (var stream = File.OpenRead(zip))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        if (!string.Equals(hash, update.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(dir, recursive: true);
            throw new InvalidDataException("O pacote baixado não confere com a assinatura (download corrompido ou alterado).");
        }

        var files = Path.Combine(dir, "files");
        ZipFile.ExtractToDirectory(zip, files); // recusa caminhos fora da pasta (zip slip)
        foreach (var required in RequiredFiles)
            if (!File.Exists(Path.Combine(files, required)))
                throw new InvalidDataException($"O pacote não contém {required}.");
        File.Delete(zip);
        return files;
    }

    /// <summary>
    /// Inicia o aplicador (o novo BackupGuard.Service.exe da pasta temporária). Ele para o serviço,
    /// troca os arquivos, inicia de novo e desfaz tudo se a nova versão não subir.
    /// </summary>
    public static void LaunchApplier(string stagedFiles, string installDir, string currentVersion, string newVersion)
    {
        var psi = new ProcessStartInfo(Path.Combine(stagedFiles, "BackupGuard.Service.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = stagedFiles,
        };
        foreach (var arg in new[] { "apply-update", stagedFiles, installDir, currentVersion, newVersion })
            psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    public UpdateResult? ConsumeResult()
    {
        if (!File.Exists(paths.UpdateResultFile))
            return null;
        try
        {
            return JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(paths.UpdateResultFile), Json.Options);
        }
        finally
        {
            File.Delete(paths.UpdateResultFile);
        }
    }

    /// <summary>Apaga pastas de versões já instaladas e restos de arquivos substituídos (.old).</summary>
    public void Cleanup(string installDir)
    {
        if (Directory.Exists(paths.UpdatesDir))
            foreach (var dir in Directory.GetDirectories(paths.UpdatesDir).Where(d => Path.GetFileName(d) != "rollback"))
                TryDelete(() => Directory.Delete(dir, recursive: true));
        if (Directory.Exists(installDir))
            foreach (var old in Directory.EnumerateFiles(installDir, "*.old", SearchOption.AllDirectories))
                TryDelete(() => File.Delete(old));
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Arquivo ainda em uso (ex.: interface aberta); sai na próxima limpeza.
        }
    }
}
