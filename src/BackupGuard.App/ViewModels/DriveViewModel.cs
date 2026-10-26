using System.Diagnostics;
using BackupGuard.App.Views;
using BackupGuard.Core;
using BackupGuard.Core.Drive;
using Google.Apis.Auth.OAuth2;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BackupGuard.App.ViewModels;

public sealed partial class DriveViewModel(MainViewModel shell)
    : PageViewModel(shell, "Google Drive", "Conta onde os backups ficam guardados", "")
{
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial string UsageText { get; set; } = "";
    [ObservableProperty] public partial double UsagePercent { get; set; }

    public string RootFolder
    {
        get => App.Config.DriveRootFolderName;
        set
        {
            App.Config.DriveRootFolderName = value.Trim();
            OnPropertyChanged();
        }
    }

    public int ScanInterval
    {
        get => App.Config.ScanIntervalSeconds;
        set
        {
            App.Config.ScanIntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    public override void OnConfigReloaded() => OnPropertyChanged(string.Empty);

    public override void Refresh()
    {
        IsConnected = App.IsGoogleConnected;
        if (Shell.DriveQuota is { } q)
        {
            UsagePercent = q.Limit is > 0 ? 100d * q.Usage / q.Limit.Value : 0;
            UsageText = q.Limit is { } limit
                ? $"{Format.Bytes(q.Usage)} usados de {Format.Bytes(limit)} · {Format.Bytes(limit - q.Usage)} livres"
                : $"{Format.Bytes(q.Usage)} usados";
        }
        else
        {
            UsagePercent = 0;
            UsageText = Shell.DriveError ?? "Consultando…";
        }
    }

    [RelayCommand]
    private Task ConnectAsync() => RunAsync(async () =>
    {
        if (App.IsDemo)
            throw new InvalidOperationException("Indisponível no modo demonstração.");

        var dialog = new OpenFileDialog
        {
            Title = "Selecione o credentials.json baixado do Google Cloud",
            Filter = "Credenciais OAuth (*.json)|*.json",
        };
        if (dialog.ShowDialog() != true)
            return;

        var client = (await GoogleClientSecrets.FromFileAsync(dialog.FileName)).Secrets;
        App.SecretStore.Update(s =>
        {
            s.GoogleClientId = client.ClientId;
            s.GoogleClientSecret = client.ClientSecret;
        });

        Dialog.Info("Autorizar no navegador",
            "O navegador vai abrir. Entre com a conta Google que vai guardar os backups e permita o acesso.\n\n" +
            "O BackupGuard só consegue ver os arquivos que ele mesmo criar no Drive.");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await GoogleDriveClient.AuthorizeInteractiveAsync(App.Paths, App.SecretStore.Load(), cts.Token);
        await Shell.RefreshDriveQuotaAsync();
        Refresh();
        await App.AuditAsync("☁️ Conta do Google Drive conectada/alterada");
        if (App.Service.GetState() == Services.ServiceRunState.Running)
            await App.Service.RestartAsync();

        if (Dialog.Confirm("Conta conectada",
                $"Tudo certo! O token foi guardado criptografado.\n\nPor segurança, apague o arquivo de credenciais:\n{dialog.FileName}",
                "Apagar o arquivo"))
            File.Delete(dialog.FileName);
    }, "Conta Google conectada");

    [RelayCommand]
    private Task RefreshUsageAsync() => RunAsync(async () =>
    {
        await Shell.RefreshDriveQuotaAsync();
        Refresh();
    });

    [RelayCommand]
    private static void OpenConsole() =>
        Process.Start(new ProcessStartInfo("https://console.cloud.google.com/apis/credentials") { UseShellExecute = true });

    [RelayCommand]
    private static void OpenDrive() =>
        Process.Start(new ProcessStartInfo("https://drive.google.com") { UseShellExecute = true });
}
