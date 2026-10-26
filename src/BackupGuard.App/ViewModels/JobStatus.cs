using BackupGuard.Core;
using BackupGuard.Core.Config;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.State;

namespace BackupGuard.App.ViewModels;

public enum HealthLevel
{
    Ok,
    Warning,
    Error,
}

/// <summary>Situação de uma tarefa calculada a partir do estado gravado pelo serviço.</summary>
public sealed record JobStatus(
    string Name,
    string Folder,
    HealthLevel Level,
    string StatusText,
    string LastBackupText,
    string DetailText,
    int WaitingFiles)
{
    public static JobStatus From(BackupJob job, JobState? state, DateTimeOffset nowUtc)
    {
        var last = state?.LastSuccessUtc;
        var level = HealthLevel.Ok;
        var status = "Em dia";

        if (last is null)
        {
            level = HealthLevel.Warning;
            status = "Sem backup ainda";
        }
        else if (TimeOnly.TryParse(job.ExpectedBackupTime, out var expected))
        {
            var due = Schedule.LastDueOccurrence(nowUtc.LocalDateTime, expected, TimeSpan.FromMinutes(job.GraceMinutes));
            if (last.Value.LocalDateTime < due.AddHours(-1))
            {
                level = HealthLevel.Error;
                status = "Atrasado";
            }
        }
        else if (nowUtc - last.Value > TimeSpan.FromHours(26))
        {
            level = HealthLevel.Warning;
            status = "Mais de 1 dia";
        }

        var detail = state?.LastArchiveSize is { } archive && state.LastSourceSize is { } source and > 0
            ? $"{Format.Bytes(source)} → {Format.Bytes(archive)} ({Format.Percent((double)archive / source)})"
            : "—";

        return new JobStatus(
            string.IsNullOrWhiteSpace(job.Name) ? job.Id : job.Name,
            job.SourceFolder,
            level,
            status,
            last is null ? "nunca" : Format.Local(last.Value),
            detail,
            CountWaiting(job));
    }

    public string WaitingText => WaitingFiles switch
    {
        0 => "pasta limpa",
        1 => "1 arquivo aguardando",
        var n => $"{n} arquivos aguardando",
    };

    private static int CountWaiting(BackupJob job)
    {
        try
        {
            if (!Directory.Exists(job.SourceFolder))
                return 0;
            return Directory.EnumerateFiles(job.SourceFolder, "*",
                    new EnumerationOptions { RecurseSubdirectories = job.IncludeSubfolders, IgnoreInaccessible = true })
                .Count(p => !ArchiveNaming.IsIgnored(p));
        }
        catch
        {
            return 0;
        }
    }
}
