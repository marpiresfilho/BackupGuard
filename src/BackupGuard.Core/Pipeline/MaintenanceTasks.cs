using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.State;
using Microsoft.Extensions.Logging;

namespace BackupGuard.Core.Pipeline;

/// <summary>Rotinas periódicas: alerta de backup que não chegou, retenção no Drive e resumo diário.</summary>
public sealed class MaintenanceTasks(
    AppConfig config,
    IDriveClientProvider driveProvider,
    DriveFolders folders,
    INotifier notifier,
    HealthPinger health,
    StateStore state,
    SummaryBuilder summary,
    TimeProvider time,
    ILogger<MaintenanceTasks> logger)
{
    public static string Version => AppInfo.Version;

    public async Task RunAsync(CancellationToken ct)
    {
        // Cada rotina isolada: falha na retenção (ex.: Drive fora do ar) não impede o alerta nem o resumo.
        foreach (var (name, step) in new (string, Func<CancellationToken, Task>)[]
                 {
                     ("alerta de backup atrasado", CheckMissingBackupsAsync),
                     ("retenção", RunRetentionIfDueAsync),
                     ("resumo diário", SendDailySummaryIfDueAsync),
                 })
        {
            try
            {
                await step(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha na rotina de {Name}", name);
            }
        }
    }

    internal async Task CheckMissingBackupsAsync(CancellationToken ct)
    {
        var nowUtc = time.GetUtcNow();
        var nowLocal = time.GetLocalNow().DateTime;
        var monitoringSince = state.Read(s => s.MonitoringSinceUtc);
        if (monitoringSince is null)
        {
            state.Update(s => s.MonitoringSinceUtc = nowUtc);
            return;
        }

        foreach (var job in config.Jobs)
        {
            if (!TimeOnly.TryParse(job.ExpectedBackupTime, out var expected))
                continue;

            var due = Schedule.LastDueOccurrence(nowLocal, expected, TimeSpan.FromMinutes(job.GraceMinutes));
            var dueUtc = new DateTimeOffset(due, time.LocalTimeZone.GetUtcOffset(due));
            if (dueUtc < monitoringSince)
                continue; // serviço instalado depois desse horário: não cobra backups anteriores

            var cs = state.Read(s => s.Jobs.GetValueOrDefault(job.Id));
            var dueDate = DateOnly.FromDateTime(due);
            // Aceita backup gerado até 1h antes do horário esperado (backup adiantado).
            if (cs?.LastSuccessUtc >= dueUtc.AddHours(-1) || cs?.LastMissingAlertDate == dueDate)
                continue;

            state.Update(s => state.Job(s, job.Id).LastMissingAlertDate = dueDate);
            var last = cs?.LastSuccessUtc is { } l ? Format.Local(l) : "nunca";
            logger.LogWarning("Backup de {Job} não chegou (esperado {Due})", job.Name, due);
            await notifier.SendAsync(
                $"🚨 <b>Backup não chegou — {Format.Html(job.Name)}</b>\n" +
                $"Esperado às {expected:HH\\:mm} de {due:dd/MM}, tolerância de {job.GraceMinutes} min.\n" +
                $"Último backup enviado: {last}\n" +
                $"Verifique se o arquivo foi gerado em <code>{Format.Html(job.SourceFolder)}</code>.", ct);
            await health.FailAsync($"{job.Id}: backup não chegou ({due:dd/MM HH:mm})", ct);
        }
    }

    internal async Task RunRetentionIfDueAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        if (state.Read(s => s.LastRetentionDate) == today)
            return;

        var drive = await driveProvider.GetAsync(ct);
        var lines = new List<string>();
        foreach (var job in config.Jobs.Where(c => c.RetentionDays is not null))
        {
            try
            {
                var folderId = await folders.ForJobAsync(job, ct);
                var backups = await drive.ListBackupsAsync(folderId, ct);
                var expired = RetentionPolicy.SelectForDeletion(backups, job.RetentionDays, job.MinBackupsToKeep, time.GetUtcNow());
                foreach (var b in expired)
                {
                    await drive.TrashAsync(b.Id, ct);
                    logger.LogInformation("Retenção: {Name} movido para a lixeira", b.Name);
                }
                if (expired.Count > 0)
                    lines.Add($"• {Format.Html(job.Name)}: {expired.Count} arquivo(s), {Format.Bytes(expired.Sum(b => b.Size))} " +
                              $"(mais antigos que {job.RetentionDays} dias)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha na retenção de {Job}", job.Name);
                lines.Add($"• {Format.Html(job.Name)}: ❌ {Format.Html(Errors.FriendlyError.From(ex).Text)}");
            }
        }

        state.Update(s => s.LastRetentionDate = today);
        if (lines.Count > 0)
            await notifier.SendAsync("🧹 <b>Retenção no Drive</b> (movidos para a lixeira, recuperáveis por 30 dias)\n" +
                                     string.Join("\n", lines), ct);
    }

    internal async Task SendDailySummaryIfDueAsync(CancellationToken ct)
    {
        if (!TimeOnly.TryParse(config.DailySummaryTime, out var at))
            return;
        var nowLocal = time.GetLocalNow().DateTime;
        var today = DateOnly.FromDateTime(nowLocal);
        if (TimeOnly.FromDateTime(nowLocal) < at || state.Read(s => s.LastSummaryDate) == today)
            return;

        state.Update(s => s.LastSummaryDate = today);
        await notifier.SendAsync(await summary.BuildAsync(ct), ct);
    }
}
