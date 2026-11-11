using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crystal.Content;

namespace Crystal.LegacyImporter;

public sealed record AssetContentImportResult(
    IReadOnlyList<ContentEntity> Entities,
    int FileCount,
    long TotalBytes);

public static class AssetContentImporter
{
    private static readonly string[] PreviewExtensions =
    {
        ".bmp", ".gif", ".jpeg", ".jpg", ".png"
    };

    public static AssetContentImportResult Map(string contentRoot)
    {
        var root = Path.GetFullPath(contentRoot);
        var candidates = new List<(string Path, string RelativePath, string AssetType)>();

        AddFiles(candidates, root, Path.Combine(root, "Maps"), "map",
            path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase));
        AddFiles(candidates, root, Path.Combine(root, "Envir", "Previews"), "preview",
            path => PreviewExtensions.Any(extension =>
                path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));

        var ordered = candidates
            .GroupBy(item => item.RelativePath, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var entities = new List<ContentEntity>(ordered.Length);
        var contentKeys = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0L;
        var localIndex = 0L;

        foreach (var asset in ordered)
        {
            var bytes = File.ReadAllBytes(asset.Path);
            totalBytes += bytes.Length;
            var sha256 = ContentJson.Sha256Hex(bytes);
            var baseContentKey = $"asset.{ToContentKeySegment(asset.RelativePath)}";
            var contentKey = MakeUniqueContentKey(baseContentKey, asset.RelativePath, contentKeys);
            var data = new AssetContentData(
                asset.RelativePath,
                Path.GetFileName(asset.RelativePath),
                Path.GetExtension(asset.RelativePath).ToLowerInvariant(),
                asset.AssetType,
                bytes.Length,
                sha256,
                asset.AssetType == "map" ? MapAssetParser.Parse(bytes) : null);

            entities.Add(new ContentEntity
            {
                EntityType = ContentEntityTypes.Asset,
                LocalIndex = ++localIndex,
                ContentKey = contentKey,
                Data = ContentJson.Canonicalize(
                    JsonSerializer.SerializeToElement(data, ContentJson.CompactOptions))
            });
        }

        return new AssetContentImportResult(entities, ordered.Length, totalBytes);
    }

    private static void AddFiles(
        ICollection<(string Path, string RelativePath, string AssetType)> candidates,
        string root,
        string directory,
        string assetType,
        Func<string, bool> include)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (!include(path)) continue;

            candidates.Add((
                path,
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                assetType));
        }
    }

    private static string ToContentKeySegment(string relativePath)
    {
        var key = Regex.Replace(relativePath.Replace('\\', '/'), @"[^A-Za-z0-9]+", ".");
        key = key.Trim('.').ToLowerInvariant();
        return key.Length == 0 ? "asset" : key;
    }

    private static string MakeUniqueContentKey(
        string baseContentKey,
        string relativePath,
        ISet<string> existing)
    {
        if (existing.Add(baseContentKey))
        {
            return baseContentKey;
        }

        var suffix = ContentJson.Sha256Hex(Encoding.UTF8.GetBytes(relativePath))[..12];
        var key = $"{baseContentKey}.{suffix}";
        if (!existing.Add(key))
        {
            throw new InvalidDataException($"Duplicate asset content key for {relativePath}.");
        }

        return key;
    }

    private sealed record AssetContentData(
        string RelativePath,
        string FileName,
        string Extension,
        string AssetType,
        long SizeBytes,
        string Sha256,
        MapAssetMetadata? Map);
}
