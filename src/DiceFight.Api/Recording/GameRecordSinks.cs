using System.Net.Http.Headers;
using System.Text.Json;

namespace DiceFight.Api.Recording;

// Where finished (and in-progress) game records go - config section
// "GameRecords":
//   Bucket     a Cloud Storage bucket name: records go there (Cloud Run)
//   LocalPath  a folder: records go there (local dev, or a mounted volume)
//   Prefix     object/folder prefix, default "dice-kingdom/games/"
// Bucket wins when both are set; neither set = recording off. On Cloud Run
// set it as an environment variable: GameRecords__Bucket=<bucket>.
public sealed class GameRecordOptions
{
    public string? Bucket { get; set; }
    public string? LocalPath { get; set; }
    public string Prefix { get; set; } = "dice-kingdom/games/";
}

// Also where player accounts are kept (Accounts/PlayerAccounts.cs), under
// their own prefix - hence the read.
public interface IGameRecordSink
{
    string Describe();
    Task WriteAsync(string name, byte[] json, CancellationToken cancel);
    // null when there is no such object.
    Task<byte[]?> ReadAsync(string name, CancellationToken cancel);
}

public sealed class LocalFolderSink(string root) : IGameRecordSink
{
    public string Describe() => $"local folder {Path.GetFullPath(root)}";

    public async Task WriteAsync(string name, byte[] json, CancellationToken cancel)
    {
        var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write-then-rename, so a reader never sees half a file.
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, json, cancel);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<byte[]?> ReadAsync(string name, CancellationToken cancel)
    {
        var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancel) : null;
    }
}

// Cloud Storage's JSON API over plain HTTP, authenticated as the Cloud Run
// service's own identity via the metadata server - no SDK, no key file.
// The service account needs write access to the bucket (Storage Object
// Creator is not enough to overwrite: records are rewritten each turn, so
// use Storage Object User, or Admin on that one bucket).
public sealed class CloudStorageSink(string bucket, HttpClient http) : IGameRecordSink
{
    private const string TokenUrl = "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token";
    private string? _token;
    private DateTime _tokenExpiresUtc;

    public string Describe() => $"Cloud Storage bucket {bucket}";

    public async Task WriteAsync(string name, byte[] json, CancellationToken cancel)
    {
        var url = $"https://storage.googleapis.com/upload/storage/v1/b/{Uri.EscapeDataString(bucket)}/o?uploadType=media&name={Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(json) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancel));
        using var response = await http.SendAsync(request, cancel);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Cloud Storage write of {name} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancel)}");
    }

    public async Task<byte[]?> ReadAsync(string name, CancellationToken cancel)
    {
        var url = $"https://storage.googleapis.com/storage/v1/b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(name)}?alt=media";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancel));
        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Cloud Storage read of {name} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancel)}");
        return await response.Content.ReadAsByteArrayAsync(cancel);
    }

    private async Task<string> TokenAsync(CancellationToken cancel)
    {
        if (_token is not null && DateTime.UtcNow < _tokenExpiresUtc) return _token;
        using var request = new HttpRequestMessage(HttpMethod.Get, TokenUrl);
        request.Headers.Add("Metadata-Flavor", "Google");
        using var response = await http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        _token = body.RootElement.GetProperty("access_token").GetString();
        _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(body.RootElement.GetProperty("expires_in").GetInt32() - 60);
        return _token!;
    }
}
