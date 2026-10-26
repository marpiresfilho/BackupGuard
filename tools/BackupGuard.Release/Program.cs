using System.Security.Cryptography;
using System.Text;
using BackupGuard.Core.Updates;

// Ferramenta de publicação (só na máquina de quem publica). A chave privada fica criptografada
// com DPAPI do usuário do Windows em %APPDATA%\BackupGuard-release\signing-key.bin.
var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "BackupGuard-release", "signing-key.bin");

const string Help = """
    BackupGuard.Release — ferramenta de publicação

      keygen                         cria a chave de assinatura e mostra a chave pública
      export-key <arquivo>           exporta a chave privada (para guardar a cópia de segurança)
      import-key <arquivo>           importa a chave privada de uma cópia de segurança
      sign <zip> <versão> [notas]    gera release.json e release.json.sig ao lado do .zip
    """;

try
{
    return args switch
    {
        ["keygen", ..] => KeyGen(args.Contains("--force")),
        ["export-key", var path] => ExportKey(path),
        ["import-key", var path] => ImportKey(path),
        ["sign", var zip, var version, .. var rest] => Sign(zip, version, rest.Length > 0 ? string.Join(' ', rest) : null),
        _ => Fail(Help),
    };
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

int KeyGen(bool force)
{
    if (File.Exists(keyFile) && !force)
        return Fail($"Já existe uma chave em {keyFile}. Use --force só se quiser trocar (as instalações atuais deixarão de aceitar atualizações).");
    var (privateKey, publicKey) = UpdateSignature.CreateKeyPair();
    SaveKey(privateKey);
    Console.WriteLine($"Chave privada salva (criptografada) em: {keyFile}");
    Console.WriteLine();
    Console.WriteLine("Chave pública (coloque em UpdateSignature.PublicKey):");
    Console.WriteLine(publicKey);
    Console.WriteLine();
    Console.WriteLine("IMPORTANTE: rode 'export-key' e guarde a cópia fora deste computador.");
    return 0;
}

int ExportKey(string path)
{
    File.WriteAllText(path, Convert.ToBase64String(LoadKey()));
    Console.WriteLine($"Chave privada exportada para {path}. Guarde em local seguro e apague este arquivo do computador.");
    return 0;
}

int ImportKey(string path)
{
    var key = Convert.FromBase64String(File.ReadAllText(path).Trim());
    using var ecdsa = ECDsa.Create();
    ecdsa.ImportPkcs8PrivateKey(key, out _); // confere se é uma chave válida
    SaveKey(key);
    Console.WriteLine("Chave importada.");
    return 0;
}

int Sign(string zip, string version, string? notes)
{
    var privateKey = LoadKey();
    var sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip)));
    var manifest = new UpdateManifest(version.TrimStart('v'), Path.GetFileName(zip), sha256, notes, DateTimeOffset.UtcNow);
    var bytes = manifest.ToBytes();
    var signature = UpdateSignature.Sign(bytes, privateKey);

    // Confere com a chave pública embutida no programa: evita publicar algo que ninguém vai aceitar.
    if (!UpdateSignature.Verify(bytes, signature))
        return Fail("A chave privada não corresponde à chave pública embutida em UpdateSignature.PublicKey.");

    var dir = Path.GetDirectoryName(Path.GetFullPath(zip))!;
    File.WriteAllBytes(Path.Combine(dir, UpdateManifest.FileName), bytes);
    File.WriteAllBytes(Path.Combine(dir, UpdateManifest.SignatureFileName), signature);
    Console.WriteLine($"Assinado: {manifest.File} v{manifest.Version} (SHA-256 {sha256[..16]}…)");
    return 0;
}

void SaveKey(byte[] key)
{
    Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
    File.WriteAllBytes(keyFile, ProtectedData.Protect(key, Encoding.UTF8.GetBytes("BackupGuard.Release"), DataProtectionScope.CurrentUser));
}

byte[] LoadKey()
{
    if (!File.Exists(keyFile))
        throw new InvalidOperationException($"Chave de assinatura não encontrada em {keyFile}. Rode 'keygen' ou 'import-key'.");
    return ProtectedData.Unprotect(File.ReadAllBytes(keyFile), Encoding.UTF8.GetBytes("BackupGuard.Release"), DataProtectionScope.CurrentUser);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
