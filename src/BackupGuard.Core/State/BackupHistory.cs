using System.Text.Json;
using System.Text.Json.Serialization;
using BackupGuard.Core.Config;

namespace BackupGuard.Core.State;

public sealed record HistoryEntry(
    DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("companyId")] string JobId, // nome usado na primeira versão do arquivo
    string SourceFile,
    bool Success,
    long SourceSize,
    long? ArchiveSize,
    string? ArchiveName,
    string? DriveFileId,
    double DurationSeconds,
    string? Error);

/// <summary>Histórico de envios, uma linha JSON por evento (lido pela interface gráfica).</summary>
public sealed class BackupHistory(AppPaths paths)
{
    private static readonly JsonSerializerOptions LineOptions = new(Json.Options) { WriteIndented = false };
    private readonly Lock _lock = new();

    public void Append(HistoryEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, LineOptions) + Environment.NewLine;
        lock (_lock) File.AppendAllText(paths.HistoryFile, line);
    }

    public IReadOnlyList<HistoryEntry> ReadLast(int count)
    {
        if (!File.Exists(paths.HistoryFile))
            return [];
        lock (_lock)
        {
            return File.ReadLines(paths.HistoryFile)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .TakeLast(count)
                .Select(l => JsonSerializer.Deserialize<HistoryEntry>(l, LineOptions)!)
                .ToList();
        }
    }
}
