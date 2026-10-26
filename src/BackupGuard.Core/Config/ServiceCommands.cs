namespace BackupGuard.Core.Config;

/// <summary>
/// Pedidos da interface gráfica para o serviço, via arquivos-sinal na pasta protegida
/// (só administradores conseguem criá-los).
/// </summary>
public sealed class ServiceCommands(AppPaths paths)
{
    /// <summary>Pede uma varredura imediata, sem esperar o tempo de estabilidade.</summary>
    public void RequestRunNow() => Touch(paths.RunNowFile);

    public bool ConsumeRunNow() => Consume(paths.RunNowFile);

    /// <summary>Pede para procurar atualização agora.</summary>
    public void RequestUpdateCheck() => Touch(paths.CheckUpdateFile);

    public bool ConsumeUpdateCheck() => Consume(paths.CheckUpdateFile);

    /// <summary>Pede para instalar a atualização disponível (mesmo com a instalação automática desligada).</summary>
    public void RequestUpdateInstall() => Touch(paths.InstallUpdateFile);

    public bool ConsumeUpdateInstall() => Consume(paths.InstallUpdateFile);

    private static void Touch(string path) => File.WriteAllText(path, DateTimeOffset.UtcNow.ToString("O"));

    private static bool Consume(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Será apagado na próxima vez; atender duas vezes não causa problema.
        }
        return true;
    }
}
