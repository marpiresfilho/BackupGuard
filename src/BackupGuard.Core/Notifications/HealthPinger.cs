using BackupGuard.Core.Config;
using Microsoft.Extensions.Logging;

namespace BackupGuard.Core.Notifications;

/// <summary>
/// Sinal para um monitor externo (healthchecks.io ou compatível). Se os sinais pararem,
/// o monitor avisa por conta própria — é a proteção contra "o PC morreu calado".
/// </summary>
public sealed class HealthPinger(HttpClient http, AppConfig config, ILogger<HealthPinger> logger)
{
    public Task SuccessAsync(string? message = null, CancellationToken ct = default) => PingAsync("", message, ct);

    public Task FailAsync(string? message = null, CancellationToken ct = default) => PingAsync("/fail", message, ct);

    private async Task PingAsync(string suffix, string? message, CancellationToken ct)
    {
        var baseUrl = config.HealthCheck.PingUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return;
        try
        {
            using var content = new StringContent(message ?? "");
            using var response = await http.PostAsync(baseUrl.TrimEnd('/') + suffix, content, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Health check respondeu {Status}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Falha no health check: {Message}", ex.Message);
        }
    }
}
