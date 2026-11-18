using System.Text.Json;

namespace Crystal.Patcher;

public static class PatchManifestLoader
{
    private const long DefaultMaximumManifestBytes = 16 * 1024 * 1024;

    public static ClientPatchManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<ClientPatchManifest>(json, PatchJson.CompactOptions);
        return manifest ?? throw new InvalidDataException("Patch manifest is empty.");
    }

    public static PatchManifestDocument LoadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        return new PatchManifestDocument(Parse(json), new Uri(fullPath), json);
    }

    public static async Task<PatchManifestDocument> LoadAsync(
        string location,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            using var ownedClient = httpClient is null ? new HttpClient() : null;
            var client = httpClient ?? ownedClient!;
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > DefaultMaximumManifestBytes)
            {
                throw new InvalidDataException("Patch manifest exceeds the maximum allowed size.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (json.Length > DefaultMaximumManifestBytes)
            {
                throw new InvalidDataException("Patch manifest exceeds the maximum allowed size.");
            }

            return new PatchManifestDocument(Parse(json), uri, json);
        }

        return LoadFile(location);
    }

    public static void Write(string path, ClientPatchManifest manifest, bool indented = true)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            manifest,
            indented ? PatchJson.IndentedOptions : PatchJson.CompactOptions);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
