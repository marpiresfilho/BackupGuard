using System.Reflection;

namespace BackupGuard.Core;

public static class AppInfo
{
    /// <summary>Repositório público de onde vêm as atualizações (fixo no código, não configurável).</summary>
    public const string UpdateRepository = "marpiresfilho/BackupGuard";

    public const string ServiceName = "BackupGuard";

    /// <summary>Versão do executável em uso (serviço ou interface).</summary>
    public static string Version { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
}
