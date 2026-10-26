using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Pipeline;

namespace BackupGuard.Service;

internal static class Periodic
{
    /// <summary>Executa <paramref name="work"/> em intervalo fixo; uma falha nunca derruba o loop.</summary>
    public static async Task RunAsync(
        TimeSpan interval, Func<CancellationToken, Task> work, ILogger logger, string name, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await work(ct);
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro inesperado em {Name}", name);
            }
        }
    }
}

/// <summary>
/// Varre as pastas em intervalo fixo e processa os backups prontos. Também atende, em até
/// 1 segundo, o pedido "Executar agora" da interface gráfica.
/// </summary>
public sealed class BackupWorker(
    BackupEngine engine, AppConfig config, ServiceCommands commands, INotifier notifier, ILogger<BackupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        engine.CleanupWorkDir();
        commands.ConsumeRunNow(); // pedido antigo, de antes do serviço subir
        // Avisar quando o serviço sobe também revela reinícios inesperados do servidor.
        await notifier.SendAsync(
            $"🟢 Serviço iniciado · v{MaintenanceTasks.Version}\n" +
            $"Monitorando: {string.Join(", ", config.Jobs.Select(c => Format.Html(c.Name)))}", stoppingToken);

        var interval = TimeSpan.FromSeconds(config.ScanIntervalSeconds);
        var nextScan = DateTimeOffset.MinValue;
        await Periodic.RunAsync(TimeSpan.FromSeconds(1), async ct =>
        {
            var forced = commands.ConsumeRunNow();
            if (!forced && DateTimeOffset.UtcNow < nextScan)
                return;
            if (forced)
                logger.LogInformation("Varredura imediata pedida pela interface");
            try
            {
                await engine.RunCycleAsync(ignoreStability: forced, ct);
            }
            finally
            {
                nextScan = DateTimeOffset.UtcNow + interval;
            }
        }, logger, "varredura", stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await notifier.SendAsync("🔴 Serviço parado", cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>Roda a cada minuto: alertas de backup atrasado, retenção e resumo diário.</summary>
public sealed class MaintenanceWorker(MaintenanceTasks tasks, ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Periodic.RunAsync(TimeSpan.FromMinutes(1), tasks.RunAsync, logger, "manutenção", stoppingToken);
}
