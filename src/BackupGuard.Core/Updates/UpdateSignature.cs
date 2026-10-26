using System.Security.Cryptography;

namespace BackupGuard.Core.Updates;

/// <summary>
/// Assinatura das atualizações (ECDSA P-256 / SHA-256). Só a chave pública fica no programa:
/// mesmo que alguém invada a conta do GitHub, não consegue publicar uma versão que seja aceita.
/// </summary>
public static class UpdateSignature
{
    /// <summary>Chave pública (SubjectPublicKeyInfo, Base64) que confere as versões publicadas.</summary>
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcLQDFE9hxpdTLmWnyHPiuOGkT12rMKRFlX04UeF1K3HYZgMAD/JNvLMCVYrropO/BUK5ObXkSIqminDwXXCPnA==";

    public static bool Verify(byte[] data, byte[] signature, string publicKey = PublicKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Usado só pela ferramenta de publicação, na máquina de quem publica.</summary>
    public static byte[] Sign(byte[] data, byte[] privateKeyPkcs8)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        return ecdsa.SignData(data, HashAlgorithmName.SHA256);
    }

    public static (byte[] PrivateKeyPkcs8, string PublicKeyBase64) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKey(), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }
}
