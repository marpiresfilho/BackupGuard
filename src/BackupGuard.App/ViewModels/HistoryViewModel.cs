using System.Collections.ObjectModel;
using BackupGuard.Core;
using BackupGuard.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupGuard.App.ViewModels;

public sealed record HistoryRow(
    string When,
    string Job,
    string File,
    string Sizes,
    string Duration,
    bool Success,
    string? Error)
{
    public HealthLevel Level => Success ? HealthLevel.Ok : HealthLevel.Error;
    public string StatusText => Success ? "Enviado" : "Falhou";
}

public sealed partial class HistoryViewModel(MainViewModel shell)
    : PageViewModel(shell, "Histórico", "Últimos envios e falhas registrados pelo serviço", "")
{
    private IReadOnlyList<HistoryEntry> _entries = [];
    private long _lastLength = -1;

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    /// <summary>"all", "ok" ou "fail".</summary>
    [ObservableProperty] public partial string Filter { get; set; } = "all";

    [ObservableProperty] public partial string SummaryText { get; set; } = "";

    partial void OnFilterChanged(string value) => Apply();

    public override void Refresh()
    {
        // Só relê o arquivo se ele mudou.
        var info = new FileInfo(App.Paths.HistoryFile);
        var length = info.Exists ? info.Length : 0;
        if (length == _lastLength)
            return;
        _lastLength = length;
        _entries = new BackupHistory(App.Paths).ReadLast(500).Reverse().ToList();
        Apply();
    }

    [RelayCommand]
    private void Reload()
    {
        _lastLength = -1;
        Refresh();
    }

    private void Apply()
    {
        var names = App.Config.Jobs.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
        Rows.Clear();
        foreach (var e in _entries.Where(e => Filter switch { "ok" => e.Success, "fail" => !e.Success, _ => true }))
        {
            Rows.Add(new HistoryRow(
                e.TimestampUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                names.GetValueOrDefault(e.JobId, e.JobId),
                Path.GetFileName(e.SourceFile),
                e.ArchiveSize is { } a ? $"{Format.Bytes(e.SourceSize)} → {Format.Bytes(a)}" : Format.Bytes(e.SourceSize),
                Format.Duration(TimeSpan.FromSeconds(e.DurationSeconds)),
                e.Success,
                e.Error));
        }

        var last30 = _entries.Where(e => e.TimestampUtc >= DateTimeOffset.UtcNow.AddDays(-30)).ToList();
        var sent = last30.Where(e => e.Success).ToList();
        SummaryText = last30.Count == 0
            ? "Nenhum envio nos últimos 30 dias."
            : $"Últimos 30 dias: {sent.Count} enviado(s), {last30.Count - sent.Count} falha(s), " +
              $"{Format.Bytes(sent.Sum(e => e.ArchiveSize ?? 0))} enviados " +
              $"(economia de {Format.Bytes(sent.Sum(e => e.SourceSize - (e.ArchiveSize ?? 0)))} com a compactação).";
    }
}
