using System.Collections.ObjectModel;
using BackupGuard.App.Services;
using BackupGuard.App.Views;
using BackupGuard.Core;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupGuard.App.ViewModels;

public sealed partial class DashboardViewModel(MainViewModel shell)
    : PageViewModel(shell, "Painel", "Situação do serviço e do último backup de cada tarefa", "")
{
    public ObservableCollection<JobStatus> Jobs { get; } = [];

    [ObservableProperty] public partial string ServiceTitle { get; set; } = "";
    [ObservableProperty] public partial string ServiceDetail { get; set; } = "";
    [ObservableProperty] public partial HealthLevel ServiceLevel { get; set; }
    [ObservableProperty] public partial bool CanStart { get; set; }
    [ObservableProperty] public partial bool CanStop { get; set; }

    [ObservableProperty] public partial double DrivePercent { get; set; }
    [ObservableProperty] public partial string DriveText { get; set; } = "Consultando…";

    public override void Refresh()
    {
        var state = Shell.ServiceRunState;
        (ServiceTitle, ServiceDetail, ServiceLevel) = state switch
        {
            ServiceRunState.Running => ("Em execução", "Inicia com o Windows, antes de qualquer login. Fechar esta janela não interrompe os backups.", HealthLevel.Ok),
            ServiceRunState.Starting => ("Iniciando…", "Aguarde alguns segundos.", HealthLevel.Warning),
            ServiceRunState.Stopping => ("Parando…", "Aguarde alguns segundos.", HealthLevel.Warning),
            ServiceRunState.Stopped => ("Parado", "Nenhum backup está sendo enviado. Inicie o serviço.", HealthLevel.Error),
            _ => ("Não instalado", "Rode scripts\\install-service.ps1 como administrador para instalar o serviço.", HealthLevel.Error),
        };
        CanStart = state == ServiceRunState.Stopped;
        CanStop = state == ServiceRunState.Running;

        var snapshot = Shell.ReadState();
        var now = DateTimeOffset.UtcNow;
        Jobs.Clear();
        foreach (var job in App.Config.Jobs)
            Jobs.Add(JobStatus.From(job, snapshot.Job(job.Id), now));

        if (Shell.DriveQuota is { } q)
        {
            DrivePercent = q.Limit is > 0 ? 100d * q.Usage / q.Limit.Value : 0;
            DriveText = q.Limit is { } limit
                ? $"{Format.Bytes(q.Usage)} de {Format.Bytes(limit)} ({Format.Percent((double)q.Usage / limit)})"
                : $"{Format.Bytes(q.Usage)} usados";
        }
        else
        {
            DrivePercent = 0;
            DriveText = Shell.DriveError ?? "Consultando…";
        }
    }

    [RelayCommand]
    private Task StartAsync() => RunAsync(async () =>
    {
        await App.Service.StartAsync();
        await App.AuditAsync("▶️ Serviço iniciado manualmente");
        Shell.RefreshLive();
        Refresh();
    }, "Serviço iniciado");

    [RelayCommand]
    private Task StopAsync() => RunAsync(async () =>
    {
        if (!Dialog.Confirm("Parar o serviço?",
                "Enquanto estiver parado, nenhum backup será enviado para a nuvem.", "Parar serviço"))
            return;
        await App.AuditAsync("⏹ Serviço sendo parado manualmente");
        await App.Service.StopAsync();
        Shell.RefreshLive();
        Refresh();
    });

    [RelayCommand]
    private Task RestartAsync() => RunAsync(async () =>
    {
        await App.Service.RestartAsync();
        Shell.RefreshLive();
        Refresh();
    }, "Serviço reiniciado");

    [RelayCommand]
    private void RunNow()
    {
        if (Shell.ServiceRunState != ServiceRunState.Running)
        {
            Dialog.Info("Serviço parado", "Inicie o serviço para executar a varredura.");
            return;
        }
        App.Commands.RequestRunNow();
        Shell.ShowToast("Varredura pedida — acompanhe pelo Telegram ou pelo Histórico");
    }

    [RelayCommand]
    private Task SendSummaryAsync() => RunAsync(async () =>
    {
        var builder = new SummaryBuilder(App.Config, new UiDriveProvider(App), new StateStore(App.Paths),
            new BackupHistory(App.Paths), TimeProvider.System);
        await App.CreateNotifier().SendAsync(await builder.BuildAsync(CancellationToken.None));
    }, "Resumo enviado ao Telegram");

    [RelayCommand]
    private Task RefreshDriveAsync() => RunAsync(async () =>
    {
        await Shell.RefreshDriveQuotaAsync();
        Refresh();
    });
}

/// <summary>Fornece o cliente do Drive para rotinas do núcleo usadas pela interface.</summary>
internal sealed class UiDriveProvider(AppServices app) : Core.Drive.IDriveClientProvider
{
    public Task<Core.Drive.IDriveClient> GetAsync(CancellationToken ct) => app.CreateDriveAsync(ct);
}
