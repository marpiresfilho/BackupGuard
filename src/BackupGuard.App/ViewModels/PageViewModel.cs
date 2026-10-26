using BackupGuard.App.Services;
using BackupGuard.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupGuard.App.ViewModels;

/// <summary>Base das telas do menu lateral.</summary>
public abstract partial class PageViewModel(MainViewModel shell, string title, string subtitle, string icon) : ObservableObject
{
    protected MainViewModel Shell { get; } = shell;
    protected AppServices App => Shell.Services;

    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    /// <summary>Glifo da fonte Segoe Fluent Icons.</summary>
    public string Icon { get; } = icon;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Chamado ao abrir a tela e a cada atualização periódica enquanto ela estiver visível.</summary>
    public virtual void Refresh() { }

    /// <summary>Chamado quando a configuração é recarregada do disco (ex.: Descartar alterações).</summary>
    public virtual void OnConfigReloaded() { }

    /// <summary>Executa uma ação demorada mostrando erro amigável se falhar.</summary>
    protected async Task RunAsync(Func<Task> action, string? success = null, string errorTitle = "Não foi possível concluir")
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await action();
            if (success is not null)
                Shell.ShowToast(success);
        }
        catch (Exception ex)
        {
            Dialog.Error(errorTitle, Core.Errors.FriendlyError.From(ex).Text);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
