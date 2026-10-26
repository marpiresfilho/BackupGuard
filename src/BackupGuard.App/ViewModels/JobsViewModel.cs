using System.Collections.ObjectModel;
using BackupGuard.App.Views;
using BackupGuard.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BackupGuard.App.ViewModels;

public sealed partial class JobsViewModel : PageViewModel
{
    public JobsViewModel(MainViewModel shell)
        : base(shell, "Tarefas", "Pastas protegidas, horário esperado do backup e quanto tempo manter na nuvem", "")
    {
        Load();
    }

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial JobItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    public override void OnConfigReloaded() => Load();

    private void Load()
    {
        var selectedId = Selected?.Model.Id;
        Jobs.Clear();
        foreach (var c in App.Config.Jobs)
            Jobs.Add(new JobItemViewModel(c));
        Selected = Jobs.FirstOrDefault(c => c.Model.Id == selectedId) ?? Jobs.FirstOrDefault();
    }

    [RelayCommand]
    private void Add()
    {
        var n = App.Config.Jobs.Count + 1;
        var id = $"tarefa{n}";
        while (App.Config.Jobs.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            id = $"tarefa{++n}";
        var model = new BackupJob { Id = id, Name = $"Nova tarefa {n}", ExpectedBackupTime = "12:00" };
        App.Config.Jobs.Add(model);
        var item = new JobItemViewModel(model);
        Jobs.Add(item);
        Selected = item;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } item)
            return;
        if (!Dialog.Confirm($"Remover {item.Name}?",
                "A pasta deixa de ser monitorada depois de salvar. Os backups que já estão no Google Drive não são apagados.",
                "Remover"))
            return;
        App.Config.Jobs.Remove(item.Model);
        var index = Jobs.IndexOf(item);
        Jobs.Remove(item);
        Selected = Jobs.Count == 0 ? null : Jobs[Math.Min(index, Jobs.Count - 1)];
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        if (Selected is null)
            return;
        var dialog = new OpenFolderDialog { Title = "Pasta com os arquivos a proteger" };
        if (Directory.Exists(Selected.SourceFolder))
            dialog.InitialDirectory = Selected.SourceFolder;
        if (dialog.ShowDialog() == true)
            Selected.SourceFolder = dialog.FolderName;
    }
}

/// <summary>
/// Edita uma tarefa. As alterações vão direto para o objeto de configuração;
/// a janela principal detecta a mudança e mostra a barra "Salvar".
/// </summary>
public sealed partial class JobItemViewModel(BackupJob model) : ObservableObject
{
    private string _lastExpectedTime = model.ExpectedBackupTime ?? "12:00";
    private int _lastRetentionDays = model.RetentionDays ?? 30;

    public BackupJob Model { get; } = model;

    public string Name
    {
        get => Model.Name;
        set => SetProperty(Model.Name, value, Model, (m, v) => m.Name = v);
    }

    public string Id
    {
        get => Model.Id;
        set => SetProperty(Model.Id, value.Trim(), Model, (m, v) => m.Id = v);
    }

    public string SourceFolder
    {
        get => Model.SourceFolder;
        set
        {
            if (SetProperty(Model.SourceFolder, value, Model, (m, v) => m.SourceFolder = v))
                OnPropertyChanged(nameof(FolderExists));
        }
    }

    public bool FolderExists => Directory.Exists(Model.SourceFolder);

    public bool IncludeSubfolders
    {
        get => Model.IncludeSubfolders;
        set => SetProperty(Model.IncludeSubfolders, value, Model, (m, v) => m.IncludeSubfolders = v);
    }

    /// <summary>Padrões separados por ponto e vírgula, ex.: "*.bak; *.fdb".</summary>
    public string Patterns
    {
        get => string.Join("; ", Model.FilePatterns);
        set
        {
            var list = value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            Model.FilePatterns = list.Count == 0 ? ["*"] : list;
            OnPropertyChanged();
        }
    }

    public int StableSeconds
    {
        get => Model.StableSeconds;
        set => SetProperty(Model.StableSeconds, value, Model, (m, v) => m.StableSeconds = v);
    }

    public bool AlertEnabled
    {
        get => Model.ExpectedBackupTime is not null;
        set
        {
            Model.ExpectedBackupTime = value ? _lastExpectedTime : null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpectedTime));
        }
    }

    public string ExpectedTime
    {
        get => Model.ExpectedBackupTime ?? _lastExpectedTime;
        set
        {
            _lastExpectedTime = value.Trim();
            if (Model.ExpectedBackupTime is not null)
                Model.ExpectedBackupTime = _lastExpectedTime;
            OnPropertyChanged();
        }
    }

    public int GraceMinutes
    {
        get => Model.GraceMinutes;
        set => SetProperty(Model.GraceMinutes, value, Model, (m, v) => m.GraceMinutes = v);
    }

    public bool NeverDelete
    {
        get => Model.RetentionDays is null;
        set
        {
            Model.RetentionDays = value ? null : _lastRetentionDays;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RetentionDays));
            OnPropertyChanged(nameof(RetentionSummary));
        }
    }

    public int RetentionDays
    {
        get => Model.RetentionDays ?? _lastRetentionDays;
        set
        {
            _lastRetentionDays = Math.Max(1, value);
            if (Model.RetentionDays is not null)
                Model.RetentionDays = _lastRetentionDays;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RetentionSummary));
        }
    }

    public int MinBackupsToKeep
    {
        get => Model.MinBackupsToKeep;
        set
        {
            if (SetProperty(Model.MinBackupsToKeep, Math.Max(1, value), Model, (m, v) => m.MinBackupsToKeep = v))
                OnPropertyChanged(nameof(RetentionSummary));
        }
    }

    public string RetentionSummary => NeverDelete
        ? "Todos os backups ficam no Drive para sempre. Acompanhe o espaço usado no Painel."
        : $"Backups com mais de {RetentionDays} dia(s) vão para a lixeira do Drive (recuperáveis por 30 dias). " +
          $"Os {MinBackupsToKeep} mais recentes nunca são apagados, mesmo vencidos.";
}
