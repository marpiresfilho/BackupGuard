using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using BackupGuard.App.Services;
using BackupGuard.App.ViewModels;
using BackupGuard.App.Views;
using BackupGuard.Core.Config;
using BackupGuard.Core.Security;

namespace BackupGuard.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // --demo: abre com dados fictícios, sem administrador e sem tocar no serviço real.
        var isDemo = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        AppPaths paths;
        if (isDemo)
        {
            paths = DemoData.Create();
        }
        else
        {
            if (!IsAdministrator())
            {
                RelaunchElevated(e.Args);
                Shutdown();
                return;
            }
            paths = new AppPaths();
            EnsureInitialized(paths);
        }

        var viewModel = new MainViewModel(new AppServices(paths, isDemo));
        var window = new MainWindow(viewModel);
        MainWindow = window;

        // --demo --snapshot <pasta>: gera uma imagem de cada tela e fecha (usado para revisar o visual).
        var snapshotIndex = Array.FindIndex(e.Args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        if (isDemo && snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length)
        {
            window.Loaded += async (_, _) =>
            {
                await Snapshots.CaptureAllAsync(window, viewModel, e.Args[snapshotIndex + 1]);
                Shutdown();
            };
        }
        window.Show();
    }

    /// <summary>Primeira abertura: cria a pasta protegida e uma configuração vazia.</summary>
    private static void EnsureInitialized(AppPaths paths)
    {
        var firstRun = !Directory.Exists(paths.Root);
        paths.EnsureDirectories();
        if (firstRun)
            FolderAcl.RestrictToAdmins(paths.Root);
        var store = new ConfigStore(paths);
        if (!store.Exists)
            store.Save(new AppConfig { Telegram = new TelegramConfig { Enabled = false } });
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Os dados ficam numa pasta só de administradores, então o app pede elevação (UAC).</summary>
    private static void RelaunchElevated(string[] args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(a => $"\"{a}\"")),
            });
        }
        catch (Win32Exception)
        {
            MessageBox.Show("O BackupGuard precisa de permissão de administrador para acessar a configuração protegida.",
                "BackupGuard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialog.Error("Erro inesperado", e.Exception.Message);
        e.Handled = true;
    }
}
