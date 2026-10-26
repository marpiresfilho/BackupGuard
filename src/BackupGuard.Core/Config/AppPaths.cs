namespace BackupGuard.Core.Config;

/// <summary>
/// Locais fixos usados pelo serviço. Tudo fica em %ProgramData%\BackupGuard,
/// pasta protegida por ACL para que apenas SYSTEM e Administradores acessem.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BackupGuard");
    }

    public string Root { get; }
    public string ConfigFile => Path.Combine(Root, "config.json");
    public string SecretsFile => Path.Combine(Root, "secrets.bin");
    public string StateFile => Path.Combine(Root, "state.json");
    public string HistoryFile => Path.Combine(Root, "history.jsonl");
    public string GoogleTokenDir => Path.Combine(Root, "google");
    public string LogsDir => Path.Combine(Root, "logs");
    public string WorkDir => Path.Combine(Root, "work");
    public string RunNowFile => Path.Combine(Root, "run-now.flag");
    public string UpdatesDir => Path.Combine(Root, "updates");
    public string CheckUpdateFile => Path.Combine(Root, "check-update.flag");
    public string InstallUpdateFile => Path.Combine(Root, "install-update.flag");
    public string UpdateResultFile => Path.Combine(Root, "update-result.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(GoogleTokenDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(WorkDir);
    }
}
