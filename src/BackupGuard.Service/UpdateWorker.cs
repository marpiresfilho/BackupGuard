using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Errors;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.State;
using BackupGuard.Core.Updates;

namespace BackupGuard.Service;

/// <summary>
/// Procura atualização no GitHub a cada N horas (ou quando a interface pede) e, se permitido,
/// instala entre um backup e outro. Também avisa no Telegram o resultado da última atualização.
/// </summary>
public sealed class UpdateWorker(
    AppConfig config,
    UpdateManager updates,
    BackupEngine engine,
    ServiceCommands commands,
    StateStore state,
    INotifier notifier,
    ILogger<UpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReportPreviousUpdateAsync(stoppingToken);
        updates.Cleanup(AppContext.BaseDirectory);

        await Periodic.RunAsync(TimeSpan.FromSeconds(5), async ct =>
        {
            var checkRequested = commands.ConsumeUpdateCheck();
            var installRequested = commands.ConsumeUpdateInstall();
            var lastCheck = state.Read(s => s.Update.LastCheckUtc);
            var due = config.Updates.Enabled &&
                      (lastCheck is null || DateTimeOffset.UtcNow - lastCheck >= TimeSpan.FromHours(Math.Max(1, config.Updates.CheckIntervalHours)));
            if (due || checkRequested || installRequested)
                await CheckAndMaybeInstallAsync(installRequested, ct);
        }, logger, "atualização", stoppingToken);
    }

    private async Task CheckAndMaybeInstallAsync(bool installRequested, CancellationToken ct)
    {
        AvailableUpdate? update;
        try
        {
            update = await updates.CheckAsync(AppInfo.Version, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var error = FriendlyError.From(ex);
            logger.LogWarning("Falha ao procurar atualização: {Technical}", error.Technical);
            state.Update(s =>
            {
                s.Update.LastCheckUtc = DateTimeOffset.UtcNow;
                s.Update.LastError = error.Text;
            });
            if (ex is InvalidDataException) // assinatura inválida: pode ser tentativa de ataque
                await notifier.SendAsync($"🚨 <b>Atualização recusada</b>\n{Format.Html(ex.Message)}", ct);
            return;
        }

        state.Update(s =>
        {
            s.Update.LastCheckUtc = DateTimeOffset.UtcNow;
            s.Update.LastError = null;
            s.Update.LatestVersion = update?.Version ?? AppInfo.Version;
            s.Update.LatestNotes = update?.Manifest.Notes;
        });
        if (update is null)
            return;

        var failedBefore = state.Read(s => s.Update.FailedVersion) == update.Version;
        var autoInstall = config.Updates.AutoInstall && !failedBefore;
        if (!autoInstall && !installRequested)
        {
            if (state.Read(s => s.Update.NotifiedVersion) != update.Version)
            {
                state.Update(s => s.Update.NotifiedVersion = update.Version);
                await notifier.SendAsync(
                    $"⬆️ <b>Nova versão disponível: {Format.Html(update.Version)}</b> (instalada: {AppInfo.Version})\n" +
                    (failedBefore ? "A instalação automática desta versão falhou antes; " : "") +
                    "Instale pela interface: Atualizações → Instalar agora.", ct);
            }
            return;
        }

        // Segura as varreduras: a troca de versão nunca acontece no meio de um backup.
        var pause = await engine.PauseAsync(ct);
        try
        {
            logger.LogInformation("Instalando atualização {Version}", update.Version);
            var staged = await updates.StageAsync(update, ct);
            await notifier.SendAsync(
                $"⬆️ <b>Instalando atualização {Format.Html(update.Version)}</b> (versão atual {AppInfo.Version}).\n" +
                "O serviço vai reiniciar em instantes." +
                (string.IsNullOrWhiteSpace(update.Manifest.Notes) ? "" : $"\n\n<i>{Format.Html(update.Manifest.Notes)}</i>"), ct);
            UpdateManager.LaunchApplier(staged, AppContext.BaseDirectory, AppInfo.Version, update.Version);
            // O aplicador vai parar este serviço; as varreduras continuam pausadas até lá.
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            pause.Dispose();
            var error = FriendlyError.From(ex);
            logger.LogError(ex, "Falha ao preparar atualização {Version}", update.Version);
            state.Update(s => s.Update.LastError = error.Text);
            await notifier.SendAsync($"⚠️ <b>Falha ao baixar a atualização {Format.Html(update.Version)}</b>\n{Format.Html(error.Text)}", ct);
        }
    }

    private async Task ReportPreviousUpdateAsync(CancellationToken ct)
    {
        UpdateResult? result;
        try
        {
            result = updates.ConsumeResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível ler o resultado da última atualização");
            return;
        }
        if (result is null)
            return;

        if (result.Success)
        {
            state.Update(s => s.Update.FailedVersion = null);
            await notifier.SendAsync($"✅ <b>Atualizado</b> da versão {Format.Html(result.FromVersion)} para {Format.Html(result.ToVersion)}.", ct);
        }
        else
        {
            state.Update(s => s.Update.FailedVersion = result.ToVersion);
            await notifier.SendAsync(
                $"⚠️ <b>A atualização para {Format.Html(result.ToVersion)} falhou</b> e a versão {Format.Html(result.FromVersion)} foi restaurada.\n" +
                $"<code>{Format.Html(result.Error)}</code>\nOs backups continuam normalmente. Detalhes em Registros (update-*.log).", ct);
        }
    }
}
