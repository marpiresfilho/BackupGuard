using System.Text.Json;
using System.Text.Json.Serialization;
using BackupGuard.Core.Config;

namespace BackupGuard.Core.State;

public sealed class ServiceState
{
    public string? DriveRootFolderId { get; set; }

    /// <summary>Quando o monitoramento começou; alertas de "backup não chegou" só valem a partir daqui.</summary>
    public DateTimeOffset? MonitoringSinceUtc { get; set; }
    [JsonPropertyName("companies")] // nome usado na primeira versão do arquivo
    public Dictionary<string, JobState> Jobs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateOnly? LastSummaryDate { get; set; }
    public DateOnly? LastRetentionDate { get; set; }

    /// <summary>
    /// Arquivos já confirmados no Drive cuja exclusão local falhou. Evita reenviar
    /// o mesmo arquivo e garante nova tentativa de apagar.
    /// </summary>
    public Dictionary<string, UploadedSource> PendingDeletes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public UpdateStatus Update { get; set; } = new();
}

/// <summary>Resultado da última procura por atualização (exibido na interface).</summary>
public sealed class UpdateStatus
{
    public DateTimeOffset? LastCheckUtc { get; set; }
    public string? LatestVersion { get; set; }
    public string? LatestNotes { get; set; }
    public string? LastError { get; set; }

    /// <summary>Versão cuja instalação falhou e foi desfeita: não é tentada de novo automaticamente.</summary>
    public string? FailedVersion { get; set; }

    /// <summary>Última versão avisada no Telegram (para não repetir o aviso).</summary>
    public string? NotifiedVersion { get; set; }
}

public sealed class JobState
{
    public string? DriveFolderId { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public string? LastArchiveName { get; set; }
    public long? LastSourceSize { get; set; }
    public long? LastArchiveSize { get; set; }
    public DateOnly? LastMissingAlertDate { get; set; }
}

public sealed record UploadedSource(string Path, long Size, DateTime LastWriteUtc, string DriveFileId);

/// <summary>Estado persistente do serviço, compartilhado entre os workers com trava.</summary>
public sealed class StateStore
{
    private readonly AppPaths _paths;
    private readonly Lock _lock = new();
    private readonly ServiceState _state;

    public StateStore(AppPaths paths)
    {
        _paths = paths;
        _state = File.Exists(paths.StateFile)
            ? JsonSerializer.Deserialize<ServiceState>(File.ReadAllText(paths.StateFile), Json.Options) ?? new()
            : new();
    }

    public T Read<T>(Func<ServiceState, T> read)
    {
        lock (_lock) return read(_state);
    }

    public void Update(Action<ServiceState> change)
    {
        lock (_lock)
        {
            change(_state);
            Json.WriteAtomic(_paths.StateFile, _state);
        }
    }

    public JobState Job(ServiceState state, string jobId)
    {
        if (!state.Jobs.TryGetValue(jobId, out var job))
            state.Jobs[jobId] = job = new JobState();
        return job;
    }
}
