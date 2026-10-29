using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.Notifications;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackupGuard.Tests;

/// <summary>
/// Alerta "backup não chegou": só deve disparar quando nenhum arquivo novo apareceu —
/// não quando o arquivo chegou e ainda está sendo compactado (caso real: máquina lenta, 5h).
/// </summary>
public sealed class MissingBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bg-miss-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly string _source;

    // 27/10 às 21:05 (horário local fixo em UTC-3): prazo de 18:00 + 3h já venceu.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 21, 5, 0, TimeSpan.FromHours(-3));

    public MissingBackupTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        _paths.EnsureDirectories();
        _source = Path.Combine(_root, "origem");
        Directory.CreateDirectory(_source);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => Zone;
    }

    private sealed class Notifier : INotifier
    {
        public readonly List<string> Messages = [];

        public Task SendAsync(string html, CancellationToken ct = default)
        {
            Messages.Add(html);
            return Task.CompletedTask;
        }
    }

    private (MaintenanceTasks Tasks, Notifier Notifier, StateStore State) Build()
    {
        var config = new AppConfig
        {
            Jobs = [new BackupJob { Id = "t", Name = "Teste", SourceFolder = _source, ExpectedBackupTime = "18:00", GraceMinutes = 180 }],
        };
        var state = new StateStore(_paths);
        state.Update(s =>
        {
            s.MonitoringSinceUtc = Now.AddDays(-5);
            state.Job(s, "t").LastSuccessUtc = Now.AddHours(-20); // backup de ontem
        });
        var notifier = new Notifier();
        var provider = new DriveClientProvider(_ => throw new InvalidOperationException("sem Drive no teste"));
        var history = new BackupHistory(_paths);
        var tasks = new MaintenanceTasks(config, provider, new DriveFolders(config, state, provider), notifier,
            new HealthPinger(new HttpClient(), config, NullLogger<HealthPinger>.Instance), state,
            new SummaryBuilder(config, provider, state, history, new FixedTime()), new FixedTime(),
            NullLogger<MaintenanceTasks>.Instance);
        return (tasks, notifier, state);
    }

    [Fact]
    public async Task Alerts_when_no_file_arrived()
    {
        var (tasks, notifier, _) = Build();
        await tasks.CheckMissingBackupsAsync(CancellationToken.None);
        Assert.Contains(notifier.Messages, m => m.Contains("Backup não chegou"));
    }

    [Fact]
    public async Task Does_not_alert_while_backup_is_being_compressed()
    {
        var (tasks, notifier, state) = Build();
        state.Update(s =>
        {
            var js = state.Job(s, "t");
            js.InProgressFile = "FullDBList_20261027_001.cbk";
            js.InProgressStage = "Compactando";
            js.InProgressSinceUtc = Now.AddHours(-2);
        });
        await tasks.CheckMissingBackupsAsync(CancellationToken.None);
        Assert.Empty(notifier.Messages);
    }

    [Fact]
    public async Task Does_not_alert_when_new_file_is_waiting_in_folder()
    {
        var (tasks, notifier, _) = Build();
        var file = Path.Combine(_source, "backup.cbk");
        await File.WriteAllTextAsync(file, "x");
        File.SetLastWriteTimeUtc(file, Now.AddHours(-2).UtcDateTime); // gerado às 19:05
        await tasks.CheckMissingBackupsAsync(CancellationToken.None);
        Assert.Empty(notifier.Messages);
    }

    [Fact]
    public async Task Old_file_left_in_folder_still_alerts()
    {
        var (tasks, notifier, _) = Build();
        var file = Path.Combine(_source, "antigo.cbk");
        await File.WriteAllTextAsync(file, "x");
        File.SetLastWriteTimeUtc(file, Now.AddDays(-3).UtcDateTime);
        await tasks.CheckMissingBackupsAsync(CancellationToken.None);
        Assert.Contains(notifier.Messages, m => m.Contains("Backup não chegou"));
    }

    [Theory]
    [InlineData(null, 9, 1.5, 4, 1)]   // máquina apertada: 1,5 GB livres no nível máximo → 1 núcleo
    [InlineData(null, 5, 1.5, 4, 2)]   // nível Normal cabe com 2 núcleos
    [InlineData(null, 9, 6.0, 4, 2)]   // RAM sobrando: limitado pela CPU (metade dos núcleos)
    [InlineData(3, 9, 0.5, 4, 3)]      // escolha manual é respeitada
    public void Chooses_threads_by_cpu_and_free_memory(int? configured, int level, double freeGb, int cpus, int expected) =>
        Assert.Equal(expected, SevenZipCompressor.ChooseThreads(configured, level, (ulong)(freeGb * (1L << 30)), cpus));
}
