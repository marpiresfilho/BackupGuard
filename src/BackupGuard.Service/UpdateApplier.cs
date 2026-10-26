using System.ServiceProcess;
using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Updates;

namespace BackupGuard.Service;

/// <summary>
/// Troca os arquivos instalados pela nova versão. Roda como processo separado (o novo
/// BackupGuard.Service.exe, a partir da pasta temporária), porque o serviço não pode
/// substituir o próprio executável enquanto está rodando.
/// Se a nova versão não subir, restaura a anterior e inicia de novo.
/// </summary>
internal static class UpdateApplier
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public static int Run(AppPaths paths, string stagedFiles, string installDir, string fromVersion, string toVersion)
    {
        var log = Path.Combine(paths.LogsDir, $"update-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        void Log(string message) => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");

        var rollback = Path.Combine(paths.UpdatesDir, "rollback");
        using var service = new ServiceController(AppInfo.ServiceName);
        try
        {
            Log($"Atualizando {fromVersion} → {toVersion}");
            Stop(service);
            Log("Serviço parado");

            if (Directory.Exists(rollback))
                Directory.Delete(rollback, recursive: true);
            CopyInstallable(installDir, rollback, replaceRunning: false);
            Log("Cópia de segurança da versão atual feita");

            CopyInstallable(stagedFiles, installDir, replaceRunning: true);
            Log("Arquivos novos copiados");

            // Gravado antes de iniciar: o serviço lê este arquivo assim que sobe, para avisar no Telegram.
            WriteResult(paths, new UpdateResult(fromVersion, toVersion, true, null, DateTimeOffset.UtcNow));
            Start(service);
            Log("Nova versão em execução");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"FALHA: {ex}");
            WriteResult(paths, new UpdateResult(fromVersion, toVersion, false, ex.Message, DateTimeOffset.UtcNow));
            try
            {
                Stop(service);
                if (Directory.Exists(rollback))
                    CopyInstallable(rollback, installDir, replaceRunning: true);
                Start(service);
                Log("Versão anterior restaurada e em execução");
            }
            catch (Exception restoreEx)
            {
                Log($"FALHA AO RESTAURAR: {restoreEx}");
            }
            return 1;
        }
    }

    private static void Stop(ServiceController service)
    {
        service.Refresh();
        if (service.Status == ServiceControllerStatus.Stopped)
            return;
        if (service.Status != ServiceControllerStatus.StopPending)
            service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
    }

    private static void Start(ServiceController service)
    {
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Running)
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, Timeout);
        }
        // Confere se continuou de pé (uma versão quebrada pode subir e cair em seguida).
        Thread.Sleep(TimeSpan.FromSeconds(10));
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Running)
            throw new InvalidOperationException("A nova versão do serviço não continuou em execução.");
    }

    /// <summary>
    /// Copia só os arquivos do programa. Com <paramref name="replaceRunning"/>, renomeia o arquivo atual
    /// para .old antes — o Windows permite renomear um .exe em uso (ex.: interface aberta).
    /// </summary>
    private static void CopyInstallable(string from, string to, bool replaceRunning)
    {
        Directory.CreateDirectory(to);
        foreach (var name in new[] { "BackupGuard.exe", "BackupGuard.Service.exe", "uninstall.ps1" })
            CopyFile(Path.Combine(from, name), Path.Combine(to, name), replaceRunning);

        var sevenZip = Path.Combine(from, "7-Zip");
        if (Directory.Exists(sevenZip))
            foreach (var file in Directory.GetFiles(sevenZip))
                CopyFile(file, Path.Combine(to, "7-Zip", Path.GetFileName(file)), replaceRunning);
    }

    private static void CopyFile(string source, string target, bool replaceRunning)
    {
        if (!File.Exists(source))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (replaceRunning && File.Exists(target))
            File.Move(target, target + ".old", overwrite: true);
        File.Copy(source, target, overwrite: true);
    }

    private static void WriteResult(AppPaths paths, UpdateResult result) => Json.WriteAtomic(paths.UpdateResultFile, result);
}
