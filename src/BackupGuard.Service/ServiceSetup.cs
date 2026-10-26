using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.Security;
using BackupGuard.Core.State;
using BackupGuard.Core.Updates;
using BackupGuard.Core;

namespace BackupGuard.Service;

public static class ServiceSetup
{
    /// <summary>Registra tudo que o serviço e os comandos de linha usam.</summary>
    public static IServiceCollection AddBackupGuard(this IServiceCollection services, AppPaths paths)
    {
        var config = new ConfigStore(paths).Load();
        var secrets = new SecretStore(paths).Load();

        services.AddSingleton(paths);
        services.AddSingleton<ServiceCommands>();
        services.AddSingleton(config);
        services.AddSingleton(secrets);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<StateStore>();
        services.AddSingleton<BackupHistory>();
        services.AddSingleton<INotifier, TelegramNotifier>();
        services.AddSingleton<HealthPinger>();
        services.AddSingleton<SevenZipCompressor>();
        services.AddSingleton<IDriveClientProvider>(new DriveClientProvider(
            async ct => await GoogleDriveClient.CreateAsync(paths, secrets, ct)));
        services.AddSingleton<DriveFolders>();
        services.AddSingleton<BackupEngine>();
        services.AddSingleton(sp => new UpdateManager(paths,
            new GitHubReleases(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, AppInfo.UpdateRepository)));
        services.AddSingleton<SummaryBuilder>();
        services.AddSingleton<MaintenanceTasks>();
        return services;
    }
}
