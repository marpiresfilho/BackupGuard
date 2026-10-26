namespace BackupGuard.Core.Pipeline;

/// <summary>
/// Considera um arquivo pronto só depois que tamanho e data de modificação ficam
/// parados pelo tempo exigido — evita enviar um arquivo que ainda está sendo gravado.
/// </summary>
public sealed class FileStabilityTracker
{
    private sealed record Observation(long Size, DateTime LastWriteUtc, DateTimeOffset UnchangedSince);

    private readonly Dictionary<string, Observation> _seen = new(StringComparer.OrdinalIgnoreCase);

    public bool IsStable(string path, long size, DateTime lastWriteUtc, DateTimeOffset now, TimeSpan required)
    {
        if (!_seen.TryGetValue(path, out var obs) || obs.Size != size || obs.LastWriteUtc != lastWriteUtc)
        {
            _seen[path] = new Observation(size, lastWriteUtc, now);
            return false;
        }
        return now - obs.UnchangedSince >= required;
    }

    public void Forget(string path) => _seen.Remove(path);

    /// <summary>Descarta arquivos que não existem mais, para o dicionário não crescer para sempre.</summary>
    public void Prune(IReadOnlySet<string> existing)
    {
        foreach (var path in _seen.Keys.Where(p => !existing.Contains(p)).ToList())
            _seen.Remove(path);
    }

    /// <summary>True se outro processo ainda está com o arquivo aberto.</summary>
    public static bool IsLocked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
