using System.Diagnostics;
using BackupGuard.Core;

namespace BackupGuard.Service;

/// <summary>
/// Garante que o serviço volte sozinho depois de reiniciar o PC. Roda a cada início do serviço,
/// para que instalações antigas recebam a configuração pela atualização automática.
/// </summary>
internal static class ServiceHardening
{
    public const string BootTaskName = @"BackupGuard\IniciarAposBoot";

    public static async Task EnsureAsync(ILogger logger)
    {
        var sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
        var steps = new (string Exe, string[] Args)[]
        {
            // Início atrasado: o Windows sobe o serviço depois do boot, sem o limite de 30 s
            // disputado com os outros serviços (estourar esse limite não aciona o reinício automático).
            (sc, ["config", AppInfo.ServiceName, "start=", "delayed-auto"]),
            // Reinicia se cair: após 1 min, 1 min, 5 min; zera a contagem a cada 24h.
            (sc, ["failure", AppInfo.ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/300000"]),
            (sc, ["failureflag", AppInfo.ServiceName, "1"]),
            // Rede de segurança: 5 min após o boot, inicia o serviço se ainda estiver parado.
            (Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                ["/Create", "/F", "/TN", BootTaskName, "/SC", "ONSTART", "/DELAY", "0005:00", "/RU", "SYSTEM", "/RL", "HIGHEST",
                 "/TR", $"{sc} start {AppInfo.ServiceName}"]),
        };

        foreach (var (exe, args) in steps)
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var arg in args)
                    psi.ArgumentList.Add(arg);
                using var process = Process.Start(psi)!;
                var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    logger.LogWarning("Falha ao configurar início automático ({Exe} {Command}): {Output}",
                        Path.GetFileName(exe), args[0], output.Trim());
            }
            catch (Exception ex)
            {
                logger.LogWarning("Falha ao configurar início automático: {Message}", ex.Message);
            }
        }
    }
}
