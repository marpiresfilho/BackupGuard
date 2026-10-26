using BackupGuard.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupGuard.App.ViewModels;

public sealed partial class NotificationsViewModel(MainViewModel shell)
    : PageViewModel(shell, "Notificações", "Avisos no Telegram e monitor externo", "")
{
    private string _lastSummaryTime = shell.Services.Config.DailySummaryTime ?? "08:00";

    [ObservableProperty] public partial bool HasToken { get; set; }

    public bool TelegramEnabled
    {
        get => App.Config.Telegram.Enabled;
        set
        {
            App.Config.Telegram.Enabled = value;
            OnPropertyChanged();
        }
    }

    public string ChatId
    {
        get => App.Config.Telegram.ChatId;
        set
        {
            App.Config.Telegram.ChatId = value.Trim();
            OnPropertyChanged();
        }
    }

    public string MachineName
    {
        get => App.Config.MachineName;
        set
        {
            App.Config.MachineName = value;
            OnPropertyChanged();
        }
    }

    public bool SummaryEnabled
    {
        get => App.Config.DailySummaryTime is not null;
        set
        {
            App.Config.DailySummaryTime = value ? _lastSummaryTime : null;
            OnPropertyChanged();
        }
    }

    public string SummaryTime
    {
        get => App.Config.DailySummaryTime ?? _lastSummaryTime;
        set
        {
            _lastSummaryTime = value.Trim();
            if (App.Config.DailySummaryTime is not null)
                App.Config.DailySummaryTime = _lastSummaryTime;
            OnPropertyChanged();
        }
    }

    public string? PingUrl
    {
        get => App.Config.HealthCheck.PingUrl;
        set
        {
            App.Config.HealthCheck.PingUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    public override void Refresh() => HasToken = !string.IsNullOrEmpty(App.SecretStore.Load().TelegramBotToken);

    public override void OnConfigReloaded()
    {
        _lastSummaryTime = App.Config.DailySummaryTime ?? "08:00";
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task ChangeTokenAsync() => RunAsync(async () =>
    {
        var token = Dialog.PromptSecret("Token do bot do Telegram",
            "Cole o token fornecido pelo @BotFather. Ele fica guardado criptografado neste computador.",
            validate: t => t.Contains(':') ? null : "Token inválido: o formato é 123456789:ABC...");
        if (token is null)
            return;
        App.SecretStore.Update(s => s.TelegramBotToken = token.Trim());
        Refresh();
        await App.AuditAsync("🔑 Token do Telegram alterado");
        if (App.Service.GetState() == Services.ServiceRunState.Running)
            await App.Service.RestartAsync();
    }, "Token salvo");

    [RelayCommand]
    private Task TestTelegramAsync() => RunAsync(async () =>
    {
        if (!HasToken || string.IsNullOrWhiteSpace(ChatId))
            throw new InvalidOperationException("Configure o token e o Chat ID antes de testar.");
        await App.CreateNotifier().SendAsync("🧪 Mensagem de teste enviada pela interface do BackupGuard");
    }, "Mensagem de teste enviada — confira o Telegram");

    [RelayCommand]
    private Task TestPingAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(PingUrl))
            throw new InvalidOperationException("Informe a URL do monitor.");
        await App.CreateHealthPinger().SuccessAsync("teste pela interface");
    }, "Sinal enviado ao monitor externo");

    [RelayCommand]
    private static void OpenHealthchecks() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://healthchecks.io") { UseShellExecute = true });
}
