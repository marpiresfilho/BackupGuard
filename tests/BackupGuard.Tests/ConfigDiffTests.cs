using BackupGuard.Core.Config;

namespace BackupGuard.Tests;

public class ConfigDiffTests
{
    private static AppConfig Sample() => new()
    {
        MachineName = "Servidor",
        Jobs =
        [
            new BackupJob { Id = "a", Name = "Financeiro", SourceFolder = @"C:\a" },
            new BackupJob { Id = "b", Name = "Estoque", SourceFolder = @"C:\b" },
        ],
    };

    [Fact]
    public void Identical_configs_have_no_changes() => Assert.Empty(ConfigDiff.Describe(Sample(), Sample()));

    [Fact]
    public void Reports_changes_in_portuguese_by_job_not_position()
    {
        var before = Sample();
        var after = Sample();
        after.Jobs.Reverse();
        after.Jobs.Single(c => c.Id == "b").RetentionDays = null;
        after.Telegram.Enabled = false;

        var changes = ConfigDiff.Describe(before, after);

        Assert.Equal(2, changes.Count);
        Assert.Contains("Tarefa Estoque · retenção (dias): 30 → nunca apagar", changes);
        Assert.Contains("Avisos no Telegram: sim → não", changes);
    }

    [Fact]
    public void Reports_added_and_removed_jobs()
    {
        var after = Sample();
        after.Jobs.RemoveAt(0);
        after.Jobs.Add(new BackupJob { Id = "c", Name = "Fiscal", SourceFolder = @"C:\c" });

        var changes = ConfigDiff.Describe(Sample(), after);

        Assert.Contains("Tarefa removida: Financeiro", changes);
        Assert.Contains("Tarefa adicionada: Fiscal", changes);
    }

    [Fact]
    public void Reads_legacy_companies_key()
    {
        var json = """{ "companies": [ { "id": "x", "name": "X", "sourceFolder": "C:\\x" } ] }""";
        var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, Json.Options)!;
        Assert.Equal("x", Assert.Single(config.Jobs).Id);
        Assert.DoesNotContain("companies", System.Text.Json.JsonSerializer.Serialize(config, Json.Options));
    }
}
