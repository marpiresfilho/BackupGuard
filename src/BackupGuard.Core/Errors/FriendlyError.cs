using System.Net;
using System.Net.Sockets;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace BackupGuard.Core.Errors;

/// <summary>
/// Erro traduzido para quem não é técnico: o que aconteceu, o que fazer e se o programa
/// tenta de novo sozinho. O texto original fica em <see cref="Technical"/> (vai para o log).
/// </summary>
public sealed record FriendlyError(string Message, string? Action, bool RetriesAutomatically, string Technical)
{
    private const string Retry = "O envio será tentado de novo automaticamente.";
    private const string Reconnect = "Abra o BackupGuard → Google Drive → Trocar conta e conecte a conta novamente.";

    /// <summary>Mensagem completa para mostrar ao usuário (tela ou Telegram, sem HTML).</summary>
    public string Text => Action is null ? Message : $"{Message} {Action}";

    public static FriendlyError From(Exception exception)
    {
        var ex = RootCause(exception);
        var technical = Describe(ex);

        switch (ex)
        {
            case TokenResponseException { Error.Error: "invalid_grant" }:
                return new("O acesso à conta Google expirou ou foi revogado.", Reconnect, false, technical);
            case TokenResponseException { Error.Error: "invalid_client" or "unauthorized_client" }:
                return new("As credenciais do BackupGuard no Google Cloud foram apagadas ou alteradas.",
                    "Gere um novo JSON de credenciais no Google Cloud e conecte a conta novamente.", false, technical);
            case TokenResponseException:
                return new("O Google recusou o acesso à conta.", Reconnect, false, technical);

            case GoogleApiException api:
                return FromGoogle(api, technical);

            case HttpRequestException or SocketException or WebException:
                return new("Sem conexão com a internet ou o Google Drive não respondeu.", Retry, true, technical);
            case TaskCanceledException or TimeoutException:
                return new("O envio demorou demais e foi interrompido (conexão lenta ou instável).", Retry, true, technical);

            case IOException io when IsDiskFull(io):
                return new("O disco deste computador está cheio; não há espaço para compactar o backup.",
                    "Libere espaço em disco. O arquivo original foi mantido.", true, technical);
            case IOException io when IsSharingViolation(io):
                return new("O arquivo está aberto por outro programa e não pôde ser lido.",
                    "Ele será enviado quando o outro programa liberar o arquivo.", true, technical);
            case UnauthorizedAccessException:
                return new("Sem permissão para acessar o arquivo ou a pasta.",
                    "Verifique as permissões da pasta para a conta SYSTEM (usada pelo serviço).", true, technical);
            case OutOfMemoryException:
                return new("Memória insuficiente para compactar.",
                    "Em Segurança, reduza o nível de compactação ou o número de núcleos.", true, technical);

            default:
                // Erros gerados pelo próprio BackupGuard já têm mensagem em português.
                return new(ex.Message, null, true, technical);
        }
    }

    private static FriendlyError FromGoogle(GoogleApiException api, string technical)
    {
        var reason = api.Error?.Errors?.FirstOrDefault()?.Reason ?? "";
        var status = (int)api.HttpStatusCode;

        if (reason is "storageQuotaExceeded" or "quotaExceeded" || api.Message.Contains("storage quota", StringComparison.OrdinalIgnoreCase))
            return new("O Google Drive está cheio.",
                "Libere espaço no Drive (esvazie a lixeira) ou reduza a retenção das tarefas. O arquivo original foi mantido.",
                true, technical);
        if (status == 429 || reason is "userRateLimitExceeded" or "rateLimitExceeded" or "sharingRateLimitExceeded")
            return new("O Google limitou temporariamente a quantidade de envios.", Retry, true, technical);
        if (status == 401 || reason is "authError")
            return new("O acesso à conta Google expirou.", Reconnect, false, technical);
        if (status == 403)
            return new("O Google Drive negou a gravação (sem permissão na pasta ou API desativada).",
                "Confira se a Google Drive API está ativada no Google Cloud e conecte a conta novamente.", false, technical);
        if (status == 404)
            return new("A pasta de backup no Google Drive não foi encontrada (pode ter sido apagada).",
                "Ela será recriada automaticamente na próxima tentativa.", true, technical);
        if (status >= 500)
            return new("O Google Drive está instável no momento.", Retry, true, technical);
        return new("O Google Drive recusou a operação.", Retry, true, technical);
    }

    /// <summary>Pula embrulhos (AggregateException, falha de upload) até a causa real.</summary>
    public static Exception RootCause(Exception ex)
    {
        while (true)
        {
            switch (ex)
            {
                case AggregateException { InnerExceptions.Count: 1 } agg:
                    ex = agg.InnerExceptions[0];
                    continue;
                case IOException { InnerException: { } inner } when inner is GoogleApiException or HttpRequestException
                    or TokenResponseException or TaskCanceledException or SocketException:
                    ex = inner;
                    continue;
                case HttpRequestException { InnerException: SocketException socket }:
                    return socket;
                default:
                    return ex;
            }
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        GoogleApiException api => $"Google {(int)api.HttpStatusCode} {api.Error?.Errors?.FirstOrDefault()?.Reason}: {api.Error?.Message ?? api.Message}",
        TokenResponseException token => $"OAuth {token.Error?.Error}: {token.Error?.ErrorDescription}",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    private static bool IsDiskFull(IOException io) => (io.HResult & 0xFFFF) is 0x70 or 0x27;

    private static bool IsSharingViolation(IOException io) => (io.HResult & 0xFFFF) is 0x20 or 0x21;
}
