using System.Text.Json;
using BackupGuard.Core.Config;

namespace BackupGuard.Core.Updates;

/// <summary>
/// Descreve uma versão publicada. Vai assinado junto com o pacote (release.json + release.json.sig):
/// a assinatura cobre a versão e o SHA-256 do .zip, então ninguém troca o arquivo nem
/// faz o programa "voltar" para uma versão antiga.
/// </summary>
public sealed record UpdateManifest(string Version, string File, string Sha256, string? Notes, DateTimeOffset PublishedUtc)
{
    public const string FileName = "release.json";
    public const string SignatureFileName = "release.json.sig";

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json.Options);

    public static UpdateManifest Parse(byte[] json) =>
        JsonSerializer.Deserialize<UpdateManifest>(json, Json.Options)
        ?? throw new InvalidDataException("release.json vazio.");
}

public static class AppVersion
{
    /// <summary>Converte "v1.2.3" ou "1.2.3" em Version (sem sufixos como "-beta").</summary>
    public static Version Parse(string text)
    {
        var clean = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(clean, out var v) ? v : new Version(0, 0);
    }

    public static bool IsNewer(string candidate, string current) => Parse(candidate) > Parse(current);
}
