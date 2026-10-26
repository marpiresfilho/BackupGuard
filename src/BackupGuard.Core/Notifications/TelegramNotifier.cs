using BackupGuard.Core.Config;
using BackupGuard.Core.Security;
using Microsoft.Extensions.Logging;

namespace BackupGuard.Core.Notifications;

public interface INotifier
{
    /// <summary>Envia mensagem em HTML. Nunca lança exceção: falha de aviso não pode parar o backup.</summary>
    Task SendAsync(string html, CancellationToken ct = default);
}

public sealed class TelegramNotifier(
    HttpClient http, AppConfig config, Secrets secrets, ILogger<TelegramNotifier> logger) : INotifier
{
    private const int MaxLength = 4000;

    public async Task SendAsync(string html, CancellationToken ct = default)
    {
        if (!config.Telegram.Enabled)
            return;
        if (string.IsNullOrWhiteSpace(secrets.TelegramBotToken) || string.IsNullOrWhiteSpace(config.Telegram.ChatId))
        {
            logger.LogWarning("Telegram ativado, mas token ou chatId não configurados.");
            return;
        }

        var text = $"<b>{Format.Html(config.MachineName)}</b>\n{html}";
        if (text.Length > MaxLength)
            text = text[..MaxLength] + "\n…";

        var url = $"https://api.telegram.org/bot{secrets.TelegramBotToken}/sendMessage";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = config.Telegram.ChatId,
                    ["text"] = text,
                    ["parse_mode"] = "HTML",
                    ["disable_web_page_preview"] = "true",
                });
                using var response = await http.PostAsync(url, content, ct);
                if (response.IsSuccessStatusCode)
                    return;
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Telegram respondeu {Status}: {Body}", (int)response.StatusCode, body);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Não loga a exceção completa: a URL contém o token do bot.
                logger.LogWarning("Falha ao enviar Telegram (tentativa {Attempt}): {Message}",
                    attempt, ex.Message.Replace(secrets.TelegramBotToken, "***"));
            }
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
