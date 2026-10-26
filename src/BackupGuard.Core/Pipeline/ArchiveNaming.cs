using System.Text;

namespace BackupGuard.Core.Pipeline;

public static class ArchiveNaming
{
    /// <summary>
    /// tarefa_2026-10-25_123000_arquivo.bak.7z — o horário vem da última gravação do
    /// arquivo original, então a ordem alfabética no Drive é a ordem cronológica.
    /// </summary>
    public static string For(string jobId, string sourcePath, DateTime lastWriteLocal)
    {
        var original = Sanitize(Path.GetFileName(sourcePath));
        return $"{jobId}_{lastWriteLocal:yyyy-MM-dd_HHmmss}_{original}.7z";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) || ch == '\'' ? '_' : ch);
        return sb.ToString();
    }

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    /// <summary>Arquivos temporários ou de sistema que nunca devem ser enviados.</summary>
    public static bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("~$", StringComparison.Ordinal)
               || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
               || IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
