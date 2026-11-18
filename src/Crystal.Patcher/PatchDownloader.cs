using System.Net;
using System.Net.Http.Headers;

namespace Crystal.Patcher;

public interface IPatchDownloader
{
    Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default);
}

public sealed class HttpPatchDownloader : IPatchDownloader, IDisposable
{
    private readonly Uri _baseUri;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public HttpPatchDownloader(Uri baseUri, HttpClient? client = null)
    {
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var uri = Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(_baseUri, url);
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"Unsupported patch download scheme '{uri.Scheme}'.");
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partialPath = destinationPath + ".part";
        var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        if (existingBytes > 0)
        {
            var resumed = await TryResumeAsync(uri, partialPath, existingBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!resumed)
            {
                File.Delete(partialPath);
                existingBytes = 0;
            }
        }

        if (existingBytes == 0)
        {
            using var response = await _client.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var destination = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        File.Move(partialPath, destinationPath, overwrite: true);
    }

    private async Task<bool> TryResumeAsync(
        Uri uri,
        string partialPath,
        long existingBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return false;
        }

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            return false;
        }

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            partialPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}

public sealed class FilePatchDownloader : IPatchDownloader
{
    private readonly Uri _baseUri;

    public FilePatchDownloader(Uri baseUri)
    {
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
    }

    public Task DownloadAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(_baseUri, url);
        if (uri.Scheme != "file")
        {
            throw new InvalidDataException($"Unsupported patch download scheme '{uri.Scheme}'.");
        }

        var sourcePath = uri.LocalPath;
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Patch source '{sourcePath}' does not exist.", sourcePath);
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        return Task.CompletedTask;
    }
}
