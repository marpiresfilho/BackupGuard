using System.Text.Json.Serialization;

namespace BackupGuard.Core.Config;

public sealed class AppConfig
{
    /// <summary>Nome desta máquina nas mensagens (ex.: "Servidor Financeiro").</summary>
    public string MachineName { get; set; } = Environment.MachineName;

    public string SevenZipPath { get; set; } = DefaultSevenZipPath();

    /// <summary>O 7-Zip que vem junto no pacote de instalação; se não houver, o instalado no Windows.</summary>
    public static string DefaultSevenZipPath()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "7-Zip", "7z.exe");
        return File.Exists(bundled) ? bundled : @"C:\Program Files\7-Zip\7z.exe";
    }

    /// <summary>Nível de compactação do 7z (0 a 9). 9 = máximo.</summary>
    public int CompressionLevel { get; set; } = 9;

    /// <summary>
    /// Threads da compactação. null = automático (metade dos núcleos, até 4), para não
    /// pesar no servidor enquanto outros programas estão rodando.
    /// </summary>
    public int? CompressionThreads { get; set; }

    /// <summary>Pasta raiz criada no Google Drive. Cada tarefa ganha uma subpasta.</summary>
    public string DriveRootFolderName { get; set; } = "BackupGuard";

    /// <summary>Intervalo entre varreduras das pastas de origem.</summary>
    public int ScanIntervalSeconds { get; set; } = 30;

    /// <summary>Horário do resumo diário no Telegram (HH:mm). Vazio desativa.</summary>
    public string? DailySummaryTime { get; set; } = "08:00";

    public TelegramConfig Telegram { get; set; } = new();
    public HealthCheckConfig HealthCheck { get; set; } = new();
    public UpdatesConfig Updates { get; set; } = new();
    public List<BackupJob> Jobs { get; set; } = [];

    /// <summary>Lê o config.json da primeira versão, que chamava as tarefas de "companies".</summary>
    [JsonPropertyName("companies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BackupJob>? LegacyCompanies
    {
        get => null;
        set
        {
            if (value is { Count: > 0 } && Jobs.Count == 0)
                Jobs = value;
        }
    }
}

public sealed class TelegramConfig
{
    public bool Enabled { get; set; } = true;
    public string ChatId { get; set; } = "";
}

/// <summary>
/// Monitor externo (ex.: healthchecks.io). Se o PC parar de mandar sinal,
/// o serviço externo avisa — funciona mesmo se o PC for comprometido.
/// </summary>
public sealed class HealthCheckConfig
{
    public string? PingUrl { get; set; }
}

public sealed class UpdatesConfig
{
    /// <summary>Procura novas versões no GitHub periodicamente.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Instala sozinho (entre um backup e outro). Desligado = só avisa.</summary>
    public bool AutoInstall { get; set; } = true;

    public int CheckIntervalHours { get; set; } = 6;
}

public sealed class BackupJob
{
    /// <summary>Identificador curto, sem espaços (usado no nome dos arquivos).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>Padrões de arquivo aceitos (ex.: "*.bak"). Vazio = todos.</summary>
    public List<string> FilePatterns { get; set; } = ["*"];

    /// <summary>Tempo que o arquivo precisa ficar sem mudar antes de ser enviado.</summary>
    public int StableSeconds { get; set; } = 120;

    /// <summary>Horário em que o arquivo de backup costuma ser gerado (HH:mm). Vazio desativa o alerta.</summary>
    public string? ExpectedBackupTime { get; set; }

    /// <summary>Tolerância após o horário esperado antes de alertar que o backup não chegou.</summary>
    public int GraceMinutes { get; set; } = 180;

    /// <summary>Dias para manter no Drive. null = nunca apagar.</summary>
    public int? RetentionDays { get; set; } = 30;

    /// <summary>Quantidade mínima de backups mais recentes que nunca é apagada, mesmo vencida.</summary>
    public int MinBackupsToKeep { get; set; } = 3;
}
