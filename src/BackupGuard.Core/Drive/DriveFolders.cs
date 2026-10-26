using BackupGuard.Core.Config;
using BackupGuard.Core.State;

namespace BackupGuard.Core.Drive;

/// <summary>
/// Resolve (e cria, se preciso) a pasta de cada tarefa no Drive: BackupGuard/&lt;Tarefa&gt;.
/// Os IDs ficam no estado e são conferidos uma vez por execução do serviço.
/// </summary>
public sealed class DriveFolders(AppConfig config, StateStore state, IDriveClientProvider provider)
{
    private readonly Dictionary<string, string> _verified = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _rootId;

    /// <summary>Descarta o ID conhecido da pasta (ex.: foi apagada no Drive) para ser procurada/recriada.</summary>
    public void Forget(string jobId)
    {
        _verified.Remove(jobId);
        state.Update(s => state.Job(s, jobId).DriveFolderId = null);
    }

    public async Task<string> ForJobAsync(BackupJob job, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_verified.TryGetValue(job.Id, out var cached))
                return cached;

            var drive = await provider.GetAsync(ct);
            if (_rootId is null)
            {
                var knownRoot = state.Read(s => s.DriveRootFolderId);
                _rootId = await drive.EnsureFolderAsync(config.DriveRootFolderName, null, knownRoot, ct);
                state.Update(s => s.DriveRootFolderId = _rootId);
            }

            var knownJob = state.Read(s => s.Jobs.GetValueOrDefault(job.Id)?.DriveFolderId);
            var folderName = string.IsNullOrWhiteSpace(job.Name) ? job.Id : job.Name;
            var id = await drive.EnsureFolderAsync(folderName, _rootId, knownJob, ct);
            state.Update(s => state.Job(s, job.Id).DriveFolderId = id);
            _verified[job.Id] = id;
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public interface IDriveClientProvider
{
    Task<IDriveClient> GetAsync(CancellationToken ct);
}

/// <summary>Cria o cliente do Drive na primeira vez que for preciso e reaproveita depois.</summary>
public sealed class DriveClientProvider(Func<CancellationToken, Task<IDriveClient>> factory) : IDriveClientProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDriveClient? _client;

    public async Task<IDriveClient> GetAsync(CancellationToken ct)
    {
        if (_client is not null)
            return _client;
        await _gate.WaitAsync(ct);
        try
        {
            return _client ??= await factory(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
