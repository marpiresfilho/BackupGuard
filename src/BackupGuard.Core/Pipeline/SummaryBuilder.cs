using System.Text;
using BackupGuard.Core.Config;
using BackupGuard.Core.Drive;
using BackupGuard.Core.State;

namespace BackupGuard.Core.Pipeline;

/// <summary>Monta o resumo diário (também usado pelo botão "Enviar resumo" da interface).</summary>
public sealed class SummaryBuilder(
    AppConfig config,
    IDriveClientProvider driveProvider,
    StateStore state,
    BackupHistory history,
    TimeProvider time)
{
    public async Task<string> BuildAsync(CancellationToken ct)
    {
        var nowUtc = time.GetUtcNow();
        var recent = history.ReadLast(500).Where(h => h.TimestampUtc >= nowUtc.AddHours(-24)).ToList();
        var sb = new StringBuilder($"📊 <b>Resumo diário</b> · v{AppInfo.Version}\n");

        foreach (var job in config.Jobs)
        {
            var cs = state.Read(s => s.Jobs.GetValueOrDefault(job.Id));
            var ok = recent.Count(h => h.JobId == job.Id && h.Success);
            var fail = recent.Count(h => h.JobId == job.Id && !h.Success);
            var waiting = CountWaiting(job);
            var icon = cs?.LastSuccessUtc >= nowUtc.AddHours(-26) ? "✅" : "⚠️";
            sb.Append($"\n{icon} <b>{Format.Html(job.Name)}</b>\n");
            sb.Append(cs?.LastSuccessUtc is { } last
                ? $"   último: {Format.Local(last)} · {Format.Bytes(cs.LastArchiveSize ?? 0)}\n"
                : "   nenhum backup enviado ainda\n");
            sb.Append($"   24h: {ok} enviado(s){(fail > 0 ? $", {fail} falha(s)" : "")}");
            if (waiting > 0)
                sb.Append($" · {waiting} arquivo(s) na pasta");
            sb.Append('\n');
        }

        sb.Append("\n💾 <b>Discos</b>\n");
        foreach (var d in DriveInfo.GetDrives().Where(d => d is { DriveType: DriveType.Fixed, IsReady: true }))
        {
            var freePct = (double)d.AvailableFreeSpace / d.TotalSize;
            sb.Append($"   {(freePct < 0.1 ? "⚠️ " : "")}{d.Name.TrimEnd('\\')} {Format.Bytes(d.AvailableFreeSpace)} livres de {Format.Bytes(d.TotalSize)} ({Format.Percent(freePct)})\n");
        }

        try
        {
            var quota = await (await driveProvider.GetAsync(ct)).GetQuotaAsync(ct);
            sb.Append(quota.Limit is { } limit
                ? $"\n☁️ <b>Drive</b>: {Format.Bytes(quota.Usage)} de {Format.Bytes(limit)} ({Format.Percent((double)quota.Usage / limit)})"
                : $"\n☁️ <b>Drive</b>: {Format.Bytes(quota.Usage)} usados");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sb.Append($"\n☁️ Drive: ❌ {Format.Html(Errors.FriendlyError.From(ex).Text)}");
        }
        return sb.ToString();
    }

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
