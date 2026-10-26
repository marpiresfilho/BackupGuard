using System.Collections.ObjectModel;
using System.Windows.Threading;
using BackupGuard.App.Services;
using BackupGuard.App.Views;
using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupGuard.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _fastTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private int _ticks;

    public MainViewModel(AppServices services)
    {
        Services = services;
        Pages =
        [
            new DashboardViewModel(this),
            new JobsViewModel(this),
            new HistoryViewModel(this),
            new NotificationsViewModel(this),
            new DriveViewModel(this),
            new SecurityViewModel(this),
            new UpdatesViewModel(this),
            new LogsViewModel(this),
        ];
        _fastTimer.Tick += (_, _) => OnTick();
        _fastTimer.Start();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = null;
        };
        // Estado do serviço e do Drive antes da primeira tela, para ela não abrir com dados vazios.
        RefreshLive();
        _ = RefreshDriveQuotaAsync();
        Current = Pages[0];
    }

    public AppServices Services { get; }
    public ObservableCollection<PageViewModel> Pages { get; }

    public string Version => AppInfo.Version;
    public bool IsDemo => Services.IsDemo;

    [ObservableProperty]
    public partial PageViewModel Current { get; set; }

    partial void OnCurrentChanged(PageViewModel value) => value.Refresh();

    // ----- Painel "ao vivo" do rodapé do menu -----
    [ObservableProperty] public partial string LiveService { get; set; } = "—";
    [ObservableProperty] public partial string LiveLastBackup { get; set; } = "—";
    [ObservableProperty] public partial string LiveQueue { get; set; } = "—";
    [ObservableProperty] public partial string LiveDisk { get; set; } = "—";
    [ObservableProperty] public partial string LiveDrive { get; set; } = "—";
    [ObservableProperty] public partial ServiceRunState ServiceRunState { get; set; }

    public string MachineName => Services.Config.MachineName;

    // ----- Barra de "alterações não salvas" -----
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial bool IsSaving { get; set; }
    [ObservableProperty] public partial string? Toast { get; set; }

    /// <summary>Último uso do Drive consultado (compartilhado com o painel e a tela do Drive).</summary>
    [ObservableProperty] public partial Core.Drive.DriveQuota? DriveQuota { get; set; }
    [ObservableProperty] public partial string? DriveError { get; set; }

    public void ShowToast(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnTick()
    {
        IsDirty = Services.IsDirty;
        if (++_ticks % 5 != 0)
            return;
        RefreshLive();
        Current.Refresh();
        if (_ticks % 600 == 0)
            _ = RefreshDriveQuotaAsync();
    }

    public ServiceStateSnapshot ReadState()
    {
        try
        {
            return new ServiceStateSnapshot(new StateStore(Services.Paths));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return new ServiceStateSnapshot(null); // o serviço está gravando o arquivo neste instante
        }
    }

    public void RefreshLive()
    {
        ServiceRunState = Services.Service.GetState();
        LiveService = ServiceRunState switch
        {
            ServiceRunState.Running => "rodando",
            ServiceRunState.Starting => "iniciando",
            ServiceRunState.Stopping => "parando",
            ServiceRunState.Stopped => "PARADO",
            _ => "não instalado",
        };

        var state = ReadState();
        var last = Services.Config.Jobs
            .Select(c => state.Job(c.Id)?.LastSuccessUtc)
            .Where(t => t is not null)
            .Max();
        LiveLastBackup = last is null ? "nunca" : last.Value.ToLocalTime().ToString("dd/MM HH:mm");

        var waiting = Services.Config.Jobs.Sum(c => JobStatus.From(c, null, DateTimeOffset.UtcNow).WaitingFiles);
        LiveQueue = waiting == 0 ? "vazia" : $"{waiting} arq.";

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Services.Paths.Root)!);
            LiveDisk = $"{drive.AvailableFreeSpace / 1024d / 1024 / 1024:0} GB";
        }
        catch
        {
            LiveDisk = "—";
        }

        LiveDrive = DriveQuota is { Limit: > 0 } q ? Format.Percent((double)q.Usage / q.Limit.Value) : DriveQuota is null ? "—" : Format.Bytes(DriveQuota.Usage);
    }

    public async Task RefreshDriveQuotaAsync()
    {
        if (!Services.IsGoogleConnected)
        {
            DriveError = Services.IsDemo ? "Modo demonstração" : "Conta Google não conectada";
            return;
        }
        try
        {
            var drive = await Services.CreateDriveAsync();
            DriveQuota = await drive.GetQuotaAsync(CancellationToken.None);
            DriveError = null;
        }
        catch (Exception ex)
        {
            DriveError = Core.Errors.FriendlyError.From(ex).Text;
        }
        RefreshLive();
        Current?.Refresh();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var errors = ConfigStore.Validate(Services.Config);
        if (errors.Count > 0)
        {
            Dialog.Error("Corrija antes de salvar", "• " + string.Join("\n• ", errors));
            return;
        }

        IsSaving = true;
        try
        {
            var changes = Services.SaveConfig();
            IsDirty = false;
            OnPropertyChanged(nameof(MachineName));
            if (changes.Count > 0)
            {
                var lines = changes.Take(15).Select(c => "• " + Format.Html(c));
                await Services.AuditAsync("⚙️ <b>Configuração alterada</b>\n" + string.Join("\n", lines) +
                                          (changes.Count > 15 ? $"\n… e mais {changes.Count - 15}" : ""));
            }

            if (Services.Service.GetState() == ServiceRunState.Running)
            {
                await Services.Service.RestartAsync();
                ShowToast("Configuração salva e serviço reiniciado");
            }
            else
            {
                ShowToast("Configuração salva");
            }
        }
        catch (Exception ex)
        {
            Dialog.Error("Erro ao salvar", Core.Errors.FriendlyError.From(ex).Text);
        }
        finally
        {
            IsSaving = false;
            RefreshLive();
        }
    }

    [RelayCommand]
    private void Discard()
    {
        Services.ReloadConfig();
        IsDirty = false;
        foreach (var page in Pages)
            page.OnConfigReloaded();
        OnPropertyChanged(nameof(MachineName));
        ShowToast("Alterações descartadas");
    }
}

/// <summary>Leitura do state.json tolerante a falhas.</summary>
public sealed class ServiceStateSnapshot(StateStore? store)
{
    public JobState? Job(string id) => store?.Read(s => s.Jobs.GetValueOrDefault(id));

    public UpdateStatus? Update => store?.Read(s => s.Update);
}
