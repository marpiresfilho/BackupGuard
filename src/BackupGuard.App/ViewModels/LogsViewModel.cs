using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupGuard.App.ViewModels;

public sealed partial class LogsViewModel(MainViewModel shell)
    : PageViewModel(shell, "Registros", "Log técnico do serviço — útil para diagnosticar problemas", "")
{
    private const int MaxLines = 400;

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string FileName { get; set; } = "";
    [ObservableProperty] public partial bool AutoRefresh { get; set; } = true;
    [ObservableProperty] public partial bool OnlyProblems { get; set; }

    partial void OnOnlyProblemsChanged(bool value) => Load();

    public override void Refresh()
    {
        if (AutoRefresh || string.IsNullOrEmpty(Text))
            Load();
    }

    [RelayCommand]
    private void Load()
    {
        var file = new DirectoryInfo(App.Paths.LogsDir).Exists
            ? new DirectoryInfo(App.Paths.LogsDir).GetFiles("service-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (file is null)
        {
            FileName = "";
            Text = "Nenhum registro ainda. O log aparece aqui quando o serviço rodar.";
            return;
        }

        FileName = file.FullName;
        try
        {
            // O serviço mantém o arquivo aberto; é preciso compartilhar leitura e escrita.
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = reader.ReadToEnd().Split('\n');
            var selected = OnlyProblems
                ? lines.Where(l => l.Contains("[WRN]") || l.Contains("[ERR]") || l.Contains("[FTL]"))
                : lines;
            Text = string.Join('\n', selected.TakeLast(MaxLines)).TrimEnd();
        }
        catch (IOException ex)
        {
            Text = "Não foi possível ler o log: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.Paths.LogsDir}\"") { UseShellExecute = true });
}
