using BackupGuard.Core.Config;
using BackupGuard.Core.State;

namespace BackupGuard.App.Services;

/// <summary>
/// Dados fictícios para abrir a interface com --demo (sem administrador e sem mexer no serviço real).
/// </summary>
public static class DemoData
{
    public static AppPaths Create()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "BackupGuard-demo"));
        paths.EnsureDirectories();
        var source1 = Path.Combine(paths.Root, "origem", "Financeiro");
        var source2 = Path.Combine(paths.Root, "origem", "Estoque");
        Directory.CreateDirectory(source1);
        Directory.CreateDirectory(source2);

        new ConfigStore(paths).Save(new AppConfig
        {
            MachineName = "Servidor Principal",
            Telegram = new TelegramConfig { Enabled = true, ChatId = "123456789" },
            Jobs =
            [
                new BackupJob
                {
                    Id = "financeiro", Name = "Financeiro", SourceFolder = source1,
                    ExpectedBackupTime = "12:00", RetentionDays = 30,
                },
                new BackupJob
                {
                    Id = "estoque", Name = "Estoque", SourceFolder = source2,
                    ExpectedBackupTime = "12:30", RetentionDays = null, FilePatterns = ["*.bak", "*.fdb"],
                },
            ],
        });

        if (File.Exists(paths.HistoryFile))
            File.Delete(paths.HistoryFile);
        var history = new BackupHistory(paths);
        var now = DateTimeOffset.UtcNow;
        var state = new StateStore(paths);
        state.Update(s =>
        {
            s.MonitoringSinceUtc = now.AddDays(-20);
            s.Update.LastCheckUtc = now.AddMinutes(-40);
            s.Update.LatestVersion = "0.2.0";
            s.Update.LatestNotes = "Exemplo de novidades: tela de restauração e melhorias no resumo diário.";
            var f = state.Job(s, "financeiro");
            f.LastSuccessUtc = now.AddHours(-3);
            f.LastArchiveName = "financeiro_2026-10-25_120412_banco.bak.7z";
            f.LastSourceSize = 8_400_000_000;
            f.LastArchiveSize = 912_000_000;
            var e = state.Job(s, "estoque");
            e.LastSuccessUtc = now.AddHours(-30);
            e.LastArchiveName = "estoque_2026-10-24_123015_estoque.fdb.7z";
            e.LastSourceSize = 3_100_000_000;
            e.LastArchiveSize = 402_000_000;
        });

        for (var day = 9; day >= 0; day--)
        {
            var t = now.AddDays(-day).AddHours(-3);
            history.Append(new HistoryEntry(t, "financeiro", @"D:\Backups\Financeiro\banco.bak", true,
                8_300_000_000 + day * 9_000_000, 905_000_000 + day * 1_000_000,
                $"financeiro_{t.LocalDateTime:yyyy-MM-dd_HHmmss}_banco.bak.7z", "x", 512 + day * 7, null));
            if (day == 0)
                continue;
            if (day == 4)
                history.Append(new HistoryEntry(t.AddMinutes(20), "estoque", @"D:\Backups\Estoque\estoque.fdb", false,
                    3_000_000_000, null, null, null, 95,
                    "Sem conexão com a internet ou o Google Drive não respondeu. O envio será tentado de novo automaticamente."));
            history.Append(new HistoryEntry(t.AddMinutes(25), "estoque", @"D:\Backups\Estoque\estoque.fdb", true,
                3_000_000_000 + day * 5_000_000, 400_000_000, $"estoque_{t.LocalDateTime:yyyy-MM-dd_HHmmss}_estoque.fdb.7z",
                "y", 230, null));
        }

        File.WriteAllLines(Path.Combine(paths.LogsDir, $"service-{DateTime.Now:yyyyMMdd}.log"),
        [
            $"{DateTime.Now:yyyy-MM-dd} 12:04:31.120 [INF] Iniciando backup de D:\\Backups\\Financeiro\\banco.bak (7,82 GB)",
            $"{DateTime.Now:yyyy-MM-dd} 12:13:02.552 [INF] Backup concluído: financeiro_banco.bak.7z (869,75 MB) em 00:08:31",
            $"{DateTime.Now:yyyy-MM-dd} 12:13:02.600 [INF] Modo demonstração: estes registros são fictícios.",
        ]);
        return paths;
    }
}
