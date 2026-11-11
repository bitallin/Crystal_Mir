using System.Security.Cryptography;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public enum ContentAssetStatus
{
    Ok,
    NotConfigured,
    NotFound,
    InvalidMetadata,
    InvalidPath,
    MissingFile,
    IntegrityMismatch
}

public sealed record ContentAssetResult(
    ContentAssetStatus Status,
    ContentEntityDto? Entity,
    string? FullPath,
    string? ContentType,
    string? Error)
{
    public bool IsOk => Status == ContentAssetStatus.Ok;
}

public sealed class ContentAssetService
{
    private readonly ControlApiOptions _options;
    private readonly ContentQueryStore _contentStore;

    public ContentAssetService(ControlApiOptions options, ContentQueryStore contentStore)
    {
        _options = options;
        _contentStore = contentStore;
    }

    public async Task<ContentAssetResult> ResolveAsync(
        long importRunId,
        long localIndex,
        CancellationToken cancellationToken)
    {
        var asset = await _contentStore.GetByLocalIndexAsync(
            importRunId,
            ContentEntityTypes.Asset,
            localIndex,
            cancellationToken);
        if (asset is null)
        {
            return new ContentAssetResult(ContentAssetStatus.NotFound, null, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(_options.AssetRoot))
        {
            return new ContentAssetResult(
                ContentAssetStatus.NotConfigured,
                asset,
                null,
                null,
                "Asset root is not configured. Set ControlApi:AssetRoot or CRYSTAL_CONTROL_ASSET_ROOT.");
        }

        var relativePath = ReadString(asset.Data, "relativePath");
        var expectedSha256 = ReadString(asset.Data, "sha256");
        var expectedSize = ReadLong(asset.Data, "sizeBytes");
        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.IsNullOrWhiteSpace(expectedSha256) ||
            expectedSize is null)
        {
            return new ContentAssetResult(
                ContentAssetStatus.InvalidMetadata,
                asset,
                null,
                null,
                "Asset entity is missing relativePath, sha256, or sizeBytes.");
        }

        var root = Path.GetFullPath(_options.AssetRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) &&
            !string.Equals(fullPath, root, StringComparison.Ordinal))
        {
            return new ContentAssetResult(
                ContentAssetStatus.InvalidPath,
                asset,
                null,
                null,
                $"Asset path '{relativePath}' escapes the configured asset root.");
        }

        if (!File.Exists(fullPath))
        {
            return new ContentAssetResult(
                ContentAssetStatus.MissingFile,
                asset,
                fullPath,
                null,
                $"Asset file '{relativePath}' does not exist under the configured asset root.");
        }

        var info = new FileInfo(fullPath);
        if (info.Length != expectedSize.Value)
        {
            return new ContentAssetResult(
                ContentAssetStatus.IntegrityMismatch,
                asset,
                fullPath,
                null,
                $"Asset size mismatch for '{relativePath}': expected {expectedSize}, actual {info.Length}.");
        }

        var actualSha256 = await HashFileAsync(fullPath, cancellationToken);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ContentAssetResult(
                ContentAssetStatus.IntegrityMismatch,
                asset,
                fullPath,
                null,
                $"Asset hash mismatch for '{relativePath}'.");
        }

        return new ContentAssetResult(
            ContentAssetStatus.Ok,
            asset,
            fullPath,
            ContentTypeFor(relativePath),
            null);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ContentTypeFor(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    private static string? ReadString(System.Text.Json.JsonElement data, string property)
    {
        return data.ValueKind == System.Text.Json.JsonValueKind.Object &&
               data.TryGetProperty(property, out var value) &&
               value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadLong(System.Text.Json.JsonElement data, string property)
    {
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !data.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == System.Text.Json.JsonValueKind.Number &&
               value.TryGetInt64(out var number)
            ? number
            : null;
    }
}
