using System.Net;
using System.Net.Sockets;
using BackupGuard.Core.Errors;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Requests;

namespace BackupGuard.Tests;

public class FriendlyErrorTests
{
    private static GoogleApiException Google(HttpStatusCode status, string reason) =>
        new("drive", "raw google message")
        {
            HttpStatusCode = status,
            Error = new RequestError { Code = (int)status, Message = "raw", Errors = [new SingleError { Reason = reason }] },
        };

    [Fact]
    public void Drive_full_is_explained()
    {
        var e = FriendlyError.From(Google(HttpStatusCode.Forbidden, "storageQuotaExceeded"));
        Assert.Equal("O Google Drive está cheio.", e.Message);
        Assert.Contains("storageQuotaExceeded", e.Technical);
    }

    [Fact]
    public void Upload_failure_wrapping_is_unwrapped()
    {
        var wrapped = new IOException("Upload não concluído (Failed).", Google(HttpStatusCode.ServiceUnavailable, "backendError"));
        var e = FriendlyError.From(wrapped);
        Assert.Equal("O Google Drive está instável no momento.", e.Message);
        Assert.True(e.RetriesAutomatically);
    }

    [Fact]
    public void Revoked_token_asks_to_reconnect()
    {
        var token = new TokenResponseException(new TokenErrorResponse { Error = "invalid_grant", ErrorDescription = "Token has been expired or revoked." });
        var e = FriendlyError.From(token);
        Assert.Contains("expirou ou foi revogado", e.Message);
        Assert.Contains("Trocar conta", e.Action);
        Assert.False(e.RetriesAutomatically);
    }

    [Fact]
    public void No_internet_is_explained() =>
        Assert.StartsWith("Sem conexão com a internet",
            FriendlyError.From(new HttpRequestException("x", new SocketException((int)SocketError.HostNotFound))).Message);

    [Fact]
    public void Own_portuguese_messages_pass_through() =>
        Assert.Equal("Senha dos arquivos não configurada.", FriendlyError.From(new InvalidOperationException("Senha dos arquivos não configurada.")).Text);
}
