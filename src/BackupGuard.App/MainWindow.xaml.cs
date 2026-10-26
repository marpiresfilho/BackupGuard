using System.ComponentModel;
using System.Windows;
using BackupGuard.App.ViewModels;
using BackupGuard.App.Views;

namespace BackupGuard.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Janela maximizada sem moldura passa um pouco da tela; a margem compensa.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty && !Dialog.Confirm("Sair sem salvar?",
                "Há alterações na configuração que não foram salvas. Elas serão perdidas.", "Sair sem salvar"))
            e.Cancel = true;
    }
}
