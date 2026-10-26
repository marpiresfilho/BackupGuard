namespace BackupGuard.Core.Pipeline;

public sealed record RemoteBackup(string Id, string Name, long Size, DateTimeOffset CreatedUtc);

public static class RetentionPolicy
{
    /// <summary>
    /// Escolhe os backups vencidos. Os <paramref name="minKeep"/> mais recentes nunca saem,
    /// mesmo vencidos: se os backups pararem de ser gerados, a retenção não apaga tudo aos poucos.
    /// </summary>
    public static IReadOnlyList<RemoteBackup> SelectForDeletion(
        IEnumerable<RemoteBackup> backups, int? retentionDays, int minKeep, DateTimeOffset now)
    {
        if (retentionDays is null)
            return [];

        var cutoff = now - TimeSpan.FromDays(retentionDays.Value);
        return backups
            .OrderByDescending(b => b.CreatedUtc)
            .Skip(Math.Max(minKeep, 1))
            .Where(b => b.CreatedUtc < cutoff)
            .ToList();
    }
}
