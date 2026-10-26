using System.Diagnostics;
using BackupGuard.App.Services;
using BackupGuard.App.Views;
using BackupGuard.Core;
using BackupGuard.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupGuard.App.ViewModels;

public sealed partial class UpdatesViewModel(MainViewModel shell)
    : PageViewModel(shell, "Atualizações", "Novas versões publicadas no GitHub, conferidas por assinatura digital", "")
{
    public string InstalledVersion => AppInfo.Version;

    [ObservableProperty] public partial string LastCheckText { get; set; } = "nunca";
    [ObservableProperty] public partial string? LatestVersion { get; set; }
    [ObservableProperty] public partial string? Notes { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool HasUpdate { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial HealthLevel StatusLevel { get; set; }

    public bool Enabled
    {
        get => App.Config.Updates.Enabled;
        set
        {
            App.Config.Updates.Enabled = value;
            OnPropertyChanged();
        }
    }

    public bool AutoInstall
    {
        get => App.Config.Updates.AutoInstall;
        set
        {
            App.Config.Updates.AutoInstall = value;
            OnPropertyChanged();
        }
    }

    public override void OnConfigReloaded() => OnPropertyChanged(string.Empty);

    public override void Refresh()
    {
        var status = Shell.ReadState().Update;
        LastCheckText = status?.LastCheckUtc is { } t ? Format.Local(t) : "nunca";
        LatestVersion = status?.LatestVersion;
        Notes = status?.LatestNotes;
        Error = status?.LastError;
        HasUpdate = LatestVersion is not null && AppVersion.IsNewer(LatestVersion, InstalledVersion);

        (StatusText, StatusLevel) = (Error, HasUpdate, status?.FailedVersion) switch
        {
            ({ } _, _, _) => ("Erro na última verificação", HealthLevel.Error),
            (_, true, { } failed) when failed == LatestVersion => ($"Versão {LatestVersion} falhou ao instalar", HealthLevel.Error),
            (_, true, _) => ($"Versão {LatestVersion} disponível", HealthLevel.Warning),
            _ when status?.LastCheckUtc is null => ("Ainda não verificado", HealthLevel.Warning),
            _ => ("Você está na versão mais recente", HealthLevel.Ok),
        };
    }

    [RelayCommand]
    private void CheckNow()
    {
        if (!EnsureServiceRunning())
            return;
        App.Commands.RequestUpdateCheck();
        Shell.ShowToast("Procurando atualização… o resultado aparece aqui em alguns segundos");
    }

    [RelayCommand]
    private async Task InstallNowAsync()
    {
        if (!EnsureServiceRunning())
            return;
        if (!Dialog.Confirm($"Instalar a versão {LatestVersion}?",
                "O serviço espera o backup em andamento terminar, instala a nova versão e reinicia. " +
                "Se algo der errado, a versão atual é restaurada automaticamente.\n\n" +
                "Feche e abra esta janela depois para usar a interface nova.", "Instalar"))
            return;
        App.Commands.RequestUpdateInstall();
        await App.AuditAsync($"⬆️ Instalação da versão {Format.Html(LatestVersion)} pedida manualmente");
        Shell.ShowToast("Instalação pedida — acompanhe pelo Telegram");
    }

    [RelayCommand]
    private static void OpenReleases() =>
        Process.Start(new ProcessStartInfo($"https://github.com/{AppInfo.UpdateRepository}/releases") { UseShellExecute = true });

    private bool EnsureServiceRunning()
    {
        if (Shell.ServiceRunState == ServiceRunState.Running)
            return true;
        Dialog.Info("Serviço parado", "As atualizações são feitas pelo serviço. Inicie o serviço no Painel.");
        return false;
    }
}
