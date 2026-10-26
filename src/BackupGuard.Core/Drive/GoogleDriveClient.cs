using System.Net;
using BackupGuard.Core.Config;
using BackupGuard.Core.Pipeline;
using BackupGuard.Core.Security;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace BackupGuard.Core.Drive;

public sealed record UploadedFile(string Id, long Size, string Md5);

public sealed record DriveQuota(long? Limit, long Usage);

public interface IDriveClient
{
    Task<string> EnsureFolderAsync(string name, string? parentId, string? knownId, CancellationToken ct);
    Task<UploadedFile> UploadAsync(string path, string folderId, string name, IDictionary<string, string> properties, CancellationToken ct);
    Task<IReadOnlyList<RemoteBackup>> ListBackupsAsync(string folderId, CancellationToken ct);
    Task TrashAsync(string fileId, CancellationToken ct);
    Task<DriveQuota> GetQuotaAsync(CancellationToken ct);
}

/// <summary>
/// Acesso ao Google Drive com escopo drive.file: o programa só enxerga os arquivos que ele mesmo criou.
/// </summary>
public sealed class GoogleDriveClient : IDriveClient
{
    private const string UserKey = "backupguard";
    private const string FolderMime = "application/vnd.google-apps.folder";
    /// <summary>Marca nos arquivos enviados, usada para listar só os nossos backups.</summary>
    public const string MarkerKey = "backupguard";

    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];
    private readonly DriveService _drive;

    private GoogleDriveClient(DriveService drive) => _drive = drive;

    /// <summary>Abre o navegador para autorizar a conta Google. Rodado uma vez, por um administrador.</summary>
    public static async Task AuthorizeInteractiveAsync(AppPaths paths, Secrets secrets, CancellationToken ct)
    {
        var store = new DpapiDataStore(paths.GoogleTokenDir);
        await store.ClearAsync();
        await GoogleWebAuthorizationBroker.AuthorizeAsync(ClientSecretsFrom(secrets), Scopes, UserKey, ct, store);
    }

    /// <summary>Cria o cliente a partir do token salvo, sem interação (uso pelo serviço).</summary>
    public static async Task<GoogleDriveClient> CreateAsync(AppPaths paths, Secrets secrets, CancellationToken ct)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = ClientSecretsFrom(secrets),
            Scopes = Scopes,
            DataStore = new DpapiDataStore(paths.GoogleTokenDir),
        });
        var token = await flow.LoadTokenAsync(UserKey, ct)
                    ?? throw new InvalidOperationException(
                        "Conta Google não autorizada. Rode 'BackupGuard.Service auth <credentials.json>' como administrador.");

        var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = new UserCredential(flow, UserKey, token),
            ApplicationName = "BackupGuard",
        });
        // Cada pedaço do upload pode demorar em links lentos; o padrão de 100s é curto demais.
        drive.HttpClient.Timeout = TimeSpan.FromMinutes(10);
        return new GoogleDriveClient(drive);
    }

    private static ClientSecrets ClientSecretsFrom(Secrets secrets)
    {
        if (string.IsNullOrWhiteSpace(secrets.GoogleClientId) || string.IsNullOrWhiteSpace(secrets.GoogleClientSecret))
            throw new InvalidOperationException(
                "Credenciais do Google não importadas. Rode 'BackupGuard.Service auth <credentials.json>'.");
        return new ClientSecrets { ClientId = secrets.GoogleClientId, ClientSecret = secrets.GoogleClientSecret };
    }

    public async Task<string> EnsureFolderAsync(string name, string? parentId, string? knownId, CancellationToken ct)
    {
        if (knownId is not null)
        {
            try
            {
                var get = _drive.Files.Get(knownId);
                get.Fields = "id,trashed";
                var existing = await get.ExecuteAsync(ct);
                if (existing.Trashed != true)
                    return existing.Id;
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Pasta apagada: procura ou recria abaixo.
            }
        }

        var list = _drive.Files.List();
        list.Q = $"mimeType='{FolderMime}' and name='{Escape(name)}' and trashed=false and '{parentId ?? "root"}' in parents";
        list.Fields = "files(id)";
        list.Spaces = "drive";
        var found = await list.ExecuteAsync(ct);
        if (found.Files.Count > 0)
            return found.Files[0].Id;

        var create = _drive.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = FolderMime,
            Parents = parentId is null ? null : [parentId],
        });
        create.Fields = "id";
        return (await create.ExecuteAsync(ct)).Id;
    }

    public async Task<UploadedFile> UploadAsync(
        string path, string folderId, string name, IDictionary<string, string> properties, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var request = _drive.Files.Create(new DriveFile
        {
            Name = name,
            Parents = [folderId],
            AppProperties = new Dictionary<string, string>(properties) { [MarkerKey] = "1" },
        }, stream, "application/x-7z-compressed");
        request.Fields = "id,size,md5Checksum";
        request.ChunkSize = ResumableUpload.MinimumChunkSize * 64; // 16 MB

        var progress = await request.UploadAsync(ct);
        if (progress.Status != UploadStatus.Completed)
            throw new IOException($"Upload não concluído ({progress.Status}).", progress.Exception);

        var file = request.ResponseBody
                   ?? throw new IOException("O Drive não devolveu os dados do arquivo enviado.");
        return new UploadedFile(file.Id, file.Size ?? -1, file.Md5Checksum ?? "");
    }

    public async Task<IReadOnlyList<RemoteBackup>> ListBackupsAsync(string folderId, CancellationToken ct)
    {
        var result = new List<RemoteBackup>();
        string? pageToken = null;
        do
        {
            var list = _drive.Files.List();
            list.Q = $"'{folderId}' in parents and trashed=false and appProperties has {{ key='{MarkerKey}' and value='1' }}";
            list.Fields = "nextPageToken, files(id,name,size,createdTime)";
            list.PageSize = 1000;
            list.PageToken = pageToken;
            var page = await list.ExecuteAsync(ct);
            result.AddRange(page.Files.Select(f =>
                new RemoteBackup(f.Id, f.Name, f.Size ?? 0, f.CreatedTimeDateTimeOffset ?? DateTimeOffset.MinValue)));
            pageToken = page.NextPageToken;
        } while (pageToken is not null);
        return result;
    }

    /// <summary>Move para a lixeira (recuperável por 30 dias), nunca apaga em definitivo.</summary>
    public Task TrashAsync(string fileId, CancellationToken ct) =>
        _drive.Files.Update(new DriveFile { Trashed = true }, fileId).ExecuteAsync(ct);

    public async Task<DriveQuota> GetQuotaAsync(CancellationToken ct)
    {
        var get = _drive.About.Get();
        get.Fields = "storageQuota(limit,usage)";
        var quota = (await get.ExecuteAsync(ct)).StorageQuota;
        return new DriveQuota(quota.Limit, quota.Usage ?? 0);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
