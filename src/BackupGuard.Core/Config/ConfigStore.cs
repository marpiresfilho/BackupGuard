using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupGuard.Core.Config;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Grava em arquivo temporário e troca, para nunca deixar um JSON pela metade.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class ConfigStore(AppPaths paths)
{
    public bool Exists => File.Exists(paths.ConfigFile);

    public AppConfig Load()
    {
        var config = LoadUnvalidated();
        var errors = Validate(config);
        if (errors.Count > 0)
            throw new InvalidOperationException("config.json inválido:\n- " + string.Join("\n- ", errors));
        return config;
    }

    /// <summary>Carrega sem validar — a interface precisa abrir mesmo com a configuração incompleta.</summary>
    public AppConfig LoadUnvalidated()
    {
        if (!Exists)
            throw new InvalidOperationException(
                $"Configuração não encontrada em {paths.ConfigFile}. Rode 'BackupGuard.Service init'.");
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(paths.ConfigFile), Json.Options)
               ?? throw new InvalidOperationException("config.json vazio.");
    }

    public void Save(AppConfig config) => Json.WriteAtomic(paths.ConfigFile, config);

    public static List<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        if (config.CompressionLevel is < 0 or > 9)
            errors.Add("compressionLevel deve estar entre 0 e 9.");
        if (config.ScanIntervalSeconds < 5)
            errors.Add("scanIntervalSeconds deve ser no mínimo 5.");
        if (!string.IsNullOrWhiteSpace(config.DailySummaryTime) && !TimeOnly.TryParse(config.DailySummaryTime, out _))
            errors.Add($"dailySummaryTime inválido: '{config.DailySummaryTime}' (use HH:mm).");
        if (config.Jobs.Count == 0)
            errors.Add("nenhuma tarefa de backup configurada.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in config.Jobs)
        {
            var label = string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name;
            if (string.IsNullOrWhiteSpace(c.Id) || c.Id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-' && ch != '_'))
                errors.Add($"tarefa '{label}': id deve ter só letras, números, '-' ou '_'.");
            else if (!ids.Add(c.Id))
                errors.Add($"tarefa '{label}': id '{c.Id}' repetido.");
            if (string.IsNullOrWhiteSpace(c.SourceFolder))
                errors.Add($"tarefa '{label}': sourceFolder vazio.");
            if (c.StableSeconds < 10)
                errors.Add($"tarefa '{label}': stableSeconds deve ser no mínimo 10.");
            if (c.RetentionDays is < 1)
                errors.Add($"tarefa '{label}': retentionDays deve ser 1 ou mais (ou null para nunca apagar).");
            if (c.MinBackupsToKeep < 1)
                errors.Add($"tarefa '{label}': minBackupsToKeep deve ser 1 ou mais.");
            if (!string.IsNullOrWhiteSpace(c.ExpectedBackupTime) && !TimeOnly.TryParse(c.ExpectedBackupTime, out _))
                errors.Add($"tarefa '{label}': expectedBackupTime inválido (use HH:mm).");
        }
        return errors;
    }
}
