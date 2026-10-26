using System.Text.Json;
using System.Text.Json.Nodes;

namespace BackupGuard.Core.Config;

/// <summary>
/// Lista, em português, o que mudou entre duas versões da configuração. Usado para avisar
/// no Telegram qualquer alteração — se alguém mexer sem autorização, você fica sabendo.
/// </summary>
public static class ConfigDiff
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["machineName"] = "Nome do computador",
        ["sevenZipPath"] = "Caminho do 7-Zip",
        ["compressionLevel"] = "Nível de compactação",
        ["compressionThreads"] = "Núcleos da compactação",
        ["driveRootFolderName"] = "Pasta no Google Drive",
        ["scanIntervalSeconds"] = "Intervalo de verificação (s)",
        ["dailySummaryTime"] = "Horário do resumo diário",
        ["telegram.enabled"] = "Avisos no Telegram",
        ["telegram.chatId"] = "Chat ID do Telegram",
        ["healthCheck.pingUrl"] = "URL do monitor externo",
        ["updates.enabled"] = "Procurar atualizações",
        ["updates.autoInstall"] = "Instalar atualizações automaticamente",
        ["updates.checkIntervalHours"] = "Intervalo de verificação de atualizações (h)",
        ["id"] = "código",
        ["name"] = "nome",
        ["sourceFolder"] = "pasta",
        ["includeSubfolders"] = "incluir subpastas",
        ["filePatterns"] = "tipos de arquivo",
        ["stableSeconds"] = "espera de estabilidade (s)",
        ["expectedBackupTime"] = "horário esperado",
        ["graceMinutes"] = "tolerância (min)",
        ["retentionDays"] = "retenção (dias)",
        ["minBackupsToKeep"] = "manter no mínimo",
    };

    /// <summary>Como mostrar "null" em cada campo (o significado muda conforme o campo).</summary>
    private static readonly Dictionary<string, string> NullMeaning = new()
    {
        ["retentionDays"] = "nunca apagar",
        ["compressionThreads"] = "automático",
        ["dailySummaryTime"] = "desativado",
        ["expectedBackupTime"] = "sem alerta",
    };

    public static List<string> Describe(AppConfig before, AppConfig after)
    {
        var a = Flatten(before);
        var b = Flatten(after);
        var changes = new List<string>();

        foreach (var job in a.Jobs.Keys.Except(b.Jobs.Keys))
            changes.Add($"Tarefa removida: {a.JobNames[job]}");
        foreach (var job in b.Jobs.Keys.Except(a.Jobs.Keys))
            changes.Add($"Tarefa adicionada: {b.JobNames[job]}");

        changes.AddRange(Compare(a.Global, b.Global, ""));
        foreach (var job in a.Jobs.Keys.Intersect(b.Jobs.Keys))
            changes.AddRange(Compare(a.Jobs[job], b.Jobs[job], $"Tarefa {b.JobNames[job]} · "));
        return changes;
    }

    private static IEnumerable<string> Compare(Dictionary<string, string?> a, Dictionary<string, string?> b, string prefix)
    {
        foreach (var key in a.Keys.Union(b.Keys).Order(StringComparer.Ordinal))
        {
            a.TryGetValue(key, out var oldValue);
            b.TryGetValue(key, out var newValue);
            if (oldValue != newValue)
                yield return $"{prefix}{Labels.GetValueOrDefault(key, key)}: {Show(key, oldValue)} → {Show(key, newValue)}";
        }
    }

    private static string Show(string key, string? value) => value switch
    {
        null => NullMeaning.GetValueOrDefault(key, "(vazio)"),
        "" => "(vazio)",
        "true" => "sim",
        "false" => "não",
        _ => value,
    };

    private sealed record Flat(
        Dictionary<string, string?> Global,
        Dictionary<string, Dictionary<string, string?>> Jobs,
        Dictionary<string, string> JobNames);

    private static Flat Flatten(AppConfig config)
    {
        var root = JsonSerializer.SerializeToNode(config, Json.Options)!.AsObject();
        // Tarefas identificadas pelo código, não pela posição na lista.
        var jobs = root["jobs"]!.AsArray();
        root.Remove("jobs");

        var global = new Dictionary<string, string?>(StringComparer.Ordinal);
        Walk(root, "", global);
        var flatJobs = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in config.Jobs)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            Walk(jobs[config.Jobs.IndexOf(job)]!, "", values);
            flatJobs[job.Id] = values;
            names[job.Id] = string.IsNullOrWhiteSpace(job.Name) ? job.Id : job.Name;
        }
        return new Flat(global, flatJobs, names);
    }

    private static void Walk(JsonNode? node, string prefix, Dictionary<string, string?> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                    Walk(value, prefix.Length == 0 ? key : $"{prefix}.{key}", result);
                break;
            case JsonArray arr:
                result[prefix] = string.Join("; ", arr.Select(v => v?.ToString()));
                break;
            case null:
                result[prefix] = null;
                break;
            default:
                result[prefix] = node.ToString();
                break;
        }
    }
}
