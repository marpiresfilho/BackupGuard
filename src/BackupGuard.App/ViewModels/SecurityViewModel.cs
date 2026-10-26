using BackupGuard.App.Views;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace BackupGuard.App.ViewModels;

public sealed partial class SecurityViewModel(MainViewModel shell)
    : PageViewModel(shell, "Segurança", "Senha dos backups, compactação e proteções", "")
{
    [ObservableProperty] public partial bool HasPassword { get; set; }
    [ObservableProperty] public partial int PreviousPasswords { get; set; }
    [ObservableProperty] public partial string SevenZipStatus { get; set; } = "";

    public int CompressionLevel
    {
        get => App.Config.CompressionLevel;
        set
        {
            App.Config.CompressionLevel = value;
            OnPropertyChanged();
        }
    }

    public int? CompressionThreads
    {
        get => App.Config.CompressionThreads;
        set
        {
            App.Config.CompressionThreads = value;
            OnPropertyChanged();
        }
    }

    public string SevenZipPath
    {
        get => App.Config.SevenZipPath;
        set
        {
            App.Config.SevenZipPath = value.Trim();
            OnPropertyChanged();
            SevenZipStatus = "";
        }
    }

    public string DataFolder => App.Paths.Root;

    public override void OnConfigReloaded() => OnPropertyChanged(string.Empty);

    public override void Refresh()
    {
        var secrets = App.SecretStore.Load();
        HasPassword = !string.IsNullOrEmpty(secrets.ArchivePassword);
        PreviousPasswords = secrets.PreviousArchivePasswords.Count;
        if (string.IsNullOrEmpty(SevenZipStatus))
            SevenZipStatus = File.Exists(SevenZipPath) ? "Encontrado" : "Não encontrado neste caminho";
    }

    [RelayCommand]
    private Task GeneratePasswordAsync() => RunAsync(async () =>
    {
        if (HasPassword && !Dialog.Confirm("Gerar nova senha?",
                "Os próximos backups usarão a nova senha. Os backups que já estão no Drive continuam com a senha antiga — " +
                "ela fica guardada como \"senha antiga\" e você também deve mantê-la anotada.", "Gerar nova"))
            return;

        var password = SecretStore.GeneratePassword();
        App.SecretStore.Update(s => s.ChangeArchivePassword(password));
        Refresh();
        Dialog.ShowSecret("Guarde esta senha",
            "Anote em um gerenciador de senhas E em papel, fora deste computador. Sem ela não é possível abrir nenhum backup — " +
            "se o computador for perdido ou atacado, ela fica perdida junto.", password);
        await App.AuditAsync("🔐 Senha dos backups alterada (gerada)");
        await RestartIfRunningAsync();
    }, "Senha salva");

    [RelayCommand]
    private Task SetPasswordAsync() => RunAsync(async () =>
    {
        var password = Dialog.PromptSecret("Definir senha dos backups",
            "Mínimo de 16 caracteres. Os backups já enviados continuam com a senha anterior.", confirm: true,
            validate: p => p.Length < 16 ? "A senha precisa ter pelo menos 16 caracteres." : null);
        if (password is null)
            return;
        App.SecretStore.Update(s => s.ChangeArchivePassword(password));
        Refresh();
        await App.AuditAsync("🔐 Senha dos backups alterada (definida manualmente)");
        await RestartIfRunningAsync();
    }, "Senha salva");

    [RelayCommand]
    private Task ShowPasswordAsync() => RunAsync(async () =>
    {
        var secrets = App.SecretStore.Load();
        if (string.IsNullOrEmpty(secrets.ArchivePassword))
            return;
        if (!Dialog.Confirm("Mostrar a senha?", "Confira se ninguém está vendo a tela.", "Mostrar"))
            return;
        await App.AuditAsync("👁 A senha dos backups foi exibida na tela");
        var old = secrets.PreviousArchivePasswords.Count == 0
            ? ""
            : "\n\nSenhas antigas (backups anteriores à troca):\n" + string.Join("\n", secrets.PreviousArchivePasswords);
        Dialog.ShowSecret("Senha atual dos backups", "Use no 7-Zip para abrir os arquivos .7z." + old, secrets.ArchivePassword);
    });

    [RelayCommand]
    private void BrowseSevenZip()
    {
        var dialog = new OpenFileDialog { Title = "Localize o 7z.exe", Filter = "7z.exe|7z.exe" };
        if (dialog.ShowDialog() == true)
            SevenZipPath = dialog.FileName;
    }

    [RelayCommand]
    private Task CheckSevenZipAsync() => RunAsync(async () =>
    {
        var zip = new SevenZipCompressor(App.Config, NullLogger<SevenZipCompressor>.Instance);
        SevenZipStatus = await zip.GetVersionAsync(CancellationToken.None);
    });

    private async Task RestartIfRunningAsync()
    {
        if (App.Service.GetState() == Services.ServiceRunState.Running)
            await App.Service.RestartAsync();
    }
}
