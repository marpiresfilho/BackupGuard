using System.Text;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace BackupGuard.Core.Security;

/// <summary>
/// Armazena o token OAuth do Google criptografado com DPAPI, em vez do token.json em texto puro.
/// </summary>
public sealed class DpapiDataStore(string directory) : IDataStore
{
    private string PathFor<T>(string key) =>
        Path.Combine(directory, $"{typeof(T).Name}-{key}.bin".Replace(Path.DirectorySeparatorChar, '_'));

    public Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(directory);
        var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
        var path = PathFor<T>(key);
        File.WriteAllBytes(path + ".tmp", Dpapi.Protect(Encoding.UTF8.GetBytes(json)));
        File.Move(path + ".tmp", path, overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        File.Delete(PathFor<T>(key));
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = PathFor<T>(key);
        if (!File.Exists(path))
            return Task.FromResult<T>(default!);
        var json = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(path)));
        return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(directory))
            foreach (var file in Directory.GetFiles(directory, "*.bin"))
                File.Delete(file);
        return Task.CompletedTask;
    }
}
