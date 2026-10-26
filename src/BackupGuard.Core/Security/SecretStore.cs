using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackupGuard.Core.Config;

namespace BackupGuard.Core.Security;

public sealed class Secrets
{
    public string? TelegramBotToken { get; set; }

    /// <summary>Senha dos arquivos .7z. Sem ela não há restauração — guarde uma cópia fora do PC.</summary>
    public string? ArchivePassword { get; set; }

    /// <summary>Senhas anteriores: backups antigos continuam com elas.</summary>
    public List<string> PreviousArchivePasswords { get; set; } = [];

    /// <summary>Troca a senha guardando a anterior, para os backups antigos continuarem restauráveis.</summary>
    public void ChangeArchivePassword(string password)
    {
        if (ArchivePassword is { Length: > 0 } old && old != password && !PreviousArchivePasswords.Contains(old))
            PreviousArchivePasswords.Add(old);
        ArchivePassword = password;
    }

    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }
}

/// <summary>
/// Criptografia via DPAPI no escopo da máquina: o serviço (SYSTEM) e um administrador
/// conseguem ler, mas o arquivo copiado para outro computador é inútil.
/// </summary>
public static class Dpapi
{
    private static readonly byte[] Entropy = "BackupGuard.v1"u8.ToArray();

    public static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);

    public static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine);
}

public sealed class SecretStore(AppPaths paths)
{
    public Secrets Load()
    {
        if (!File.Exists(paths.SecretsFile))
            return new Secrets();
        var json = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(paths.SecretsFile)));
        return JsonSerializer.Deserialize<Secrets>(json, Json.Options) ?? new Secrets();
    }

    public void Save(Secrets secrets)
    {
        var data = Dpapi.Protect(JsonSerializer.SerializeToUtf8Bytes(secrets, Json.Options));
        var tmp = paths.SecretsFile + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, paths.SecretsFile, overwrite: true);
    }

    public void Update(Action<Secrets> change)
    {
        var secrets = Load();
        change(secrets);
        Save(secrets);
    }

    /// <summary>Senha forte e legível (sem caracteres ambíguos) para os arquivos .7z.</summary>
    public static string GeneratePassword(int length = 32)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(alphabet, length);
    }
}
