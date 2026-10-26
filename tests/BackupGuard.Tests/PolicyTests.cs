using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Pipeline;

namespace BackupGuard.Tests;

public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static List<RemoteBackup> Daily(int days) =>
        Enumerable.Range(0, days).Select(i => new RemoteBackup($"id{i}", $"b{i}", 100, Now.AddDays(-i))).ToList();

    [Fact]
    public void Null_retention_never_deletes()
    {
        Assert.Empty(RetentionPolicy.SelectForDeletion(Daily(400), null, 3, Now));
    }

    [Fact]
    public void Deletes_only_backups_older_than_retention()
    {
        var expired = RetentionPolicy.SelectForDeletion(Daily(40), 30, 3, Now);
        Assert.Equal(9, expired.Count); // dias 31..39
        Assert.All(expired, b => Assert.True(b.CreatedUtc < Now.AddDays(-30)));
    }

    [Fact]
    public void Keeps_minimum_even_if_all_expired()
    {
        // Backups pararam há 100 dias: os 3 últimos backups continuam no Drive.
        var old = Daily(10).Select(b => b with { CreatedUtc = b.CreatedUtc.AddDays(-100) }).ToList();
        var expired = RetentionPolicy.SelectForDeletion(old, 30, 3, Now);
        Assert.Equal(7, expired.Count);
        Assert.DoesNotContain(expired, b => b.Id is "id0" or "id1" or "id2");
    }
}

public class ScheduleTests
{
    [Fact]
    public void Before_grace_ends_uses_previous_day()
    {
        var due = Schedule.LastDueOccurrence(new DateTime(2026, 10, 25, 13, 0, 0), new TimeOnly(12, 0), TimeSpan.FromHours(3));
        Assert.Equal(new DateTime(2026, 10, 24, 12, 0, 0), due);
    }

    [Fact]
    public void After_grace_uses_today()
    {
        var due = Schedule.LastDueOccurrence(new DateTime(2026, 10, 25, 15, 30, 0), new TimeOnly(12, 0), TimeSpan.FromHours(3));
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0), due);
    }

    [Fact]
    public void Handles_grace_crossing_midnight()
    {
        var due = Schedule.LastDueOccurrence(new DateTime(2026, 10, 26, 1, 0, 0), new TimeOnly(23, 0), TimeSpan.FromHours(3));
        Assert.Equal(new DateTime(2026, 10, 24, 23, 0, 0), due);
        var later = Schedule.LastDueOccurrence(new DateTime(2026, 10, 26, 2, 30, 0), new TimeOnly(23, 0), TimeSpan.FromHours(3));
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0), later);
    }
}

public class FileStabilityTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Write = new(2026, 10, 25, 11, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Stable_only_after_required_time_unchanged()
    {
        var tracker = new FileStabilityTracker();
        var wait = TimeSpan.FromMinutes(2);
        Assert.False(tracker.IsStable("a", 10, Write, T0, wait));
        Assert.False(tracker.IsStable("a", 10, Write, T0.AddMinutes(1), wait));
        Assert.True(tracker.IsStable("a", 10, Write, T0.AddMinutes(2), wait));
    }

    [Fact]
    public void Growing_file_restarts_the_clock()
    {
        var tracker = new FileStabilityTracker();
        var wait = TimeSpan.FromMinutes(2);
        tracker.IsStable("a", 10, Write, T0, wait);
        Assert.False(tracker.IsStable("a", 20, Write, T0.AddMinutes(3), wait));
        Assert.False(tracker.IsStable("a", 20, Write, T0.AddMinutes(4), wait));
        Assert.True(tracker.IsStable("a", 20, Write, T0.AddMinutes(5), wait));
    }
}

public class ArchiveNamingTests
{
    [Fact]
    public void Name_has_job_timestamp_and_original()
    {
        var name = ArchiveNaming.For("financeiro", @"D:\Backups\base 01.bak", new DateTime(2026, 10, 25, 12, 30, 5));
        Assert.Equal("financeiro_2026-10-25_123005_base 01.bak.7z", name);
    }

    [Theory]
    [InlineData(@"C:\x\~$temp.bak", true)]
    [InlineData(@"C:\x\dump.tmp", true)]
    [InlineData(@"C:\x\desktop.ini", true)]
    [InlineData(@"C:\x\base.bak", false)]
    public void Ignores_temporary_files(string path, bool ignored) => Assert.Equal(ignored, ArchiveNaming.IsIgnored(path));
}

public class ConfigValidationTests
{
    private static AppConfig Valid() => new()
    {
        Jobs = [new BackupJob { Id = "a", Name = "A", SourceFolder = @"C:\a" }],
    };

    [Fact]
    public void Valid_config_has_no_errors() => Assert.Empty(ConfigStore.Validate(Valid()));

    [Fact]
    public void Rejects_duplicate_ids_and_bad_values()
    {
        var config = Valid();
        config.Jobs.Add(new BackupJob { Id = "A", Name = "Dup", SourceFolder = @"C:\b", RetentionDays = 0 });
        config.Jobs.Add(new BackupJob { Id = "com espaço", Name = "X", SourceFolder = "" });
        var errors = ConfigStore.Validate(config);
        Assert.Contains(errors, e => e.Contains("repetido"));
        Assert.Contains(errors, e => e.Contains("retentionDays"));
        Assert.Contains(errors, e => e.Contains("id deve ter"));
        Assert.Contains(errors, e => e.Contains("sourceFolder"));
    }

    [Fact]
    public void Rejects_empty_job_list() =>
        Assert.Contains(ConfigStore.Validate(new AppConfig()), e => e.Contains("nenhuma tarefa"));
}

public class FormatTests
{
    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Bytes_in_ptbr(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));
}
