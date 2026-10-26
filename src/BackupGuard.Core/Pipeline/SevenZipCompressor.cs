using System.Diagnostics;
using BackupGuard.Core.Config;
using Microsoft.Extensions.Logging;

namespace BackupGuard.Core.Pipeline;

/// <summary>
/// Compacta com 7-Zip (LZMA2) e criptografa com AES-256, incluindo os nomes dos arquivos (-mhe).
/// O resultado abre em qualquer 7-Zip com a senha, mesmo sem este programa.
/// </summary>
public sealed class SevenZipCompressor(AppConfig config, ILogger<SevenZipCompressor> logger)
{
    public Task CompressAsync(string source, string archive, string password, CancellationToken ct)
    {
        var threads = config.CompressionThreads ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        return RunAsync(
        [
            "a", "-t7z", $"-mx={config.CompressionLevel}", "-m0=lzma2", $"-mmt={threads}", "-mhe=on",
            "-bso0", "-bsp0", "-y", $"-p{password}", "--", archive, source,
        ], ct);
    }

    /// <summary>Testa o arquivo recém-criado (descompacta em memória e confere o CRC).</summary>
    public Task TestAsync(string archive, string password, CancellationToken ct) =>
        RunAsync(["t", "-bso0", "-bsp0", $"-p{password}", "--", archive], ct);

    public async Task<string> GetVersionAsync(CancellationToken ct)
    {
        var (_, output) = await StartAsync(["i"], ct, captureStdout: true);
        return output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("7-Zip")) ?? "7-Zip";
    }

    private async Task RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var (exitCode, stderr) = await StartAsync(args, ct, captureStdout: false);
        if (exitCode != 0)
            throw new IOException(ExplainExitCode(exitCode, stderr.Trim()));
    }

    /// <summary>Traduz os códigos de saída documentados do 7-Zip.</summary>
    internal static string ExplainExitCode(int exitCode, string stderr)
    {
        var detail = string.IsNullOrEmpty(stderr) ? "" : $" Detalhe: {stderr}";
        return exitCode switch
        {
            1 => "O 7-Zip não conseguiu ler o arquivo inteiro (provavelmente está em uso por outro programa)." + detail,
            2 when stderr.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) =>
                "Senha incorreta para este arquivo.",
            2 when stderr.Contains("space", StringComparison.OrdinalIgnoreCase) =>
                "O disco ficou cheio durante a compactação. Libere espaço em disco.",
            2 => "O 7-Zip encontrou um erro grave ao compactar ou testar o arquivo." + detail,
            7 => "O 7-Zip recusou os parâmetros (verifique o caminho do 7z.exe em Segurança)." + detail,
            8 => "Memória insuficiente para compactar. Em Segurança, reduza o nível de compactação ou o número de núcleos.",
            255 => "A compactação foi interrompida.",
            _ => $"O 7-Zip terminou com erro (código {exitCode})." + detail,
        };
    }

    private async Task<(int ExitCode, string Output)> StartAsync(IReadOnlyList<string> args, CancellationToken ct, bool captureStdout)
    {
        if (!File.Exists(config.SevenZipPath))
            throw new FileNotFoundException($"7-Zip não encontrado em '{config.SevenZipPath}'.");

        // A senha vai na linha de comando: o serviço roda como SYSTEM, e só administradores
        // conseguem ler a linha de comando de um processo do SYSTEM.
        var psi = new ProcessStartInfo(config.SevenZipPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new IOException("Não foi possível iniciar o 7-Zip.");
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Não foi possível baixar a prioridade do 7-Zip: {Message}", ex.Message);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, captureStdout ? await stdout : await stderr);
    }
}
