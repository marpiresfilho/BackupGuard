using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackupGuard.App.Services;

/// <summary>
/// Acesso compartilhado aos dados do serviço. A interface edita uma cópia da configuração
/// (<see cref="Config"/>) e só grava no disco quando o usuário clica em Salvar.
/// </summary>
public sealed class AppServices
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AppServices(AppPaths paths, bool isDemo)
    {
        Paths = paths;
        IsDemo = isDemo;
        ConfigStore = new ConfigStore(paths);
        SecretStore = new SecretStore(paths);
        Commands = new ServiceCommands(paths);
        Service = isDemo ? new DemoServiceControl() : new ServiceControl();
        ReloadConfig();
    }

    public AppPaths Paths { get; }
    public bool IsDemo { get; }
    public ConfigStore ConfigStore { get; }
    public SecretStore SecretStore { get; }
    public ServiceCommands Commands { get; }
    public IServiceControl Service { get; }

    /// <summary>Cópia de trabalho, ligada diretamente aos campos da interface.</summary>
    public AppConfig Config { get; private set; } = new();

    private string _savedJson = "";

    public bool IsDirty => Serialize(Config) != _savedJson;

    public void ReloadConfig()
    {
        Config = ConfigStore.LoadUnvalidated();
        _savedJson = Serialize(Config);
    }

    /// <summary>Grava a configuração e devolve a lista do que mudou (para o aviso no Telegram).</summary>
    public List<string> SaveConfig()
    {
        var before = ConfigStore.LoadUnvalidated();
        var changes = ConfigDiff.Describe(before, Config);
        ConfigStore.Save(Config);
        _savedJson = Serialize(Config);
        return changes;
    }

    public INotifier CreateNotifier() =>
        new TelegramNotifier(Http, Config, SecretStore.Load(), NullLogger<TelegramNotifier>.Instance);

    public HealthPinger CreateHealthPinger() => new(Http, Config, NullLogger<HealthPinger>.Instance);

    public async Task<IDriveClient> CreateDriveAsync(CancellationToken ct = default)
    {
        if (IsDemo)
            throw new InvalidOperationException("Indisponível no modo demonstração.");
        return await GoogleDriveClient.CreateAsync(Paths, SecretStore.Load(), ct);
    }

    public bool IsGoogleConnected =>
        Directory.Exists(Paths.GoogleTokenDir) && Directory.EnumerateFiles(Paths.GoogleTokenDir, "*.bin").Any();

    /// <summary>Avisa no Telegram uma ação feita pela interface, com o usuário do Windows.</summary>
    public Task AuditAsync(string html)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        return CreateNotifier().SendAsync($"{html}\n👤 {Format.Html(user)} (pela interface)");
    }

    private static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Json.Options);
}
