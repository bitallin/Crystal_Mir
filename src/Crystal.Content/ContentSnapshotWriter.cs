using System.Text;
using System.Text.Json;

namespace Crystal.Content;

public sealed class ContentSnapshotWriteOptions
{
    public required string OutputDirectory { get; init; }

    public required string ReleaseVersion { get; init; }

    public required string Source { get; init; }

    public string? SourceHash { get; init; }

    public string Generator { get; init; } = ContentSnapshotFormat.DefaultGenerator;

    public DateTimeOffset? CreatedAt { get; init; }

    public JsonElement? Legacy { get; init; }

    public bool Force { get; init; }
}

public static class ContentSnapshotWriter
{
    public static ContentSnapshotManifest Write(
        ContentSnapshotWriteOptions options,
        IEnumerable<ContentEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(entities);

        var ordered = ValidateAndOrder(entities);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        var entitiesDirectory = Path.Combine(outputDirectory, "entities");

        if (Directory.Exists(outputDirectory) &&
            Directory.EnumerateFileSystemEntries(outputDirectory).Any() &&
            !options.Force)
        {
            throw new InvalidOperationException(
                $"Output directory is not empty: {outputDirectory}. Use --force to replace snapshot files.");
        }

        Directory.CreateDirectory(entitiesDirectory);
        if (options.Force)
        {
            foreach (var file in Directory.GetFiles(entitiesDirectory))
            {
                File.Delete(file);
            }

            foreach (var name in new[] { "manifest.json", "summary.json" })
            {
                var path = Path.Combine(outputDirectory, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        var files = new SortedDictionary<string, ContentSnapshotFile>(StringComparer.Ordinal);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var group in ordered.GroupBy(entity => entity.EntityType, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var fileName = $"{group.Key}.jsonl";
            var filePath = Path.Combine(entitiesDirectory, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                   {
                       NewLine = "\n"
                   })
            {
                foreach (var entity in group)
                {
                    var canonical = new ContentEntity
                    {
                        EntityType = entity.EntityType,
                        LocalIndex = entity.LocalIndex,
                        ContentKey = entity.ContentKey,
                        SchemaVersion = entity.SchemaVersion,
                        Data = ContentJson.Canonicalize(entity.Data)
                    };
                    writer.WriteLine(JsonSerializer.Serialize(canonical, ContentJson.CompactOptions));
                }
            }

            var bytes = File.ReadAllBytes(filePath);
            counts[group.Key] = group.Count();
            files[group.Key] = new ContentSnapshotFile
            {
                File = $"entities/{fileName}",
                Sha256 = ContentJson.Sha256Hex(bytes),
                SizeBytes = bytes.LongLength,
                Count = group.Count()
            };
        }

        var contentHash = ComputeContentHash(files);
        var manifest = new ContentSnapshotManifest
        {
            ReleaseVersion = options.ReleaseVersion,
            Source = options.Source,
            SourceHash = options.SourceHash,
            Generator = options.Generator,
            CreatedAt = options.CreatedAt ?? DateTimeOffset.UtcNow,
            ContentHash = contentHash,
            Entities = files,
            Counts = counts,
            Legacy = options.Legacy
        };

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ContentJson.IndentedOptions) + "\n");
        File.WriteAllText(
            Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(new
            {
                manifest.ReleaseVersion,
                manifest.Source,
                manifest.SourceHash,
                manifest.ContentHash,
                manifest.Counts
            }, ContentJson.IndentedOptions) + "\n");

        return manifest;
    }

    public static IReadOnlyList<ContentEntity> ValidateAndOrder(IEnumerable<ContentEntity> entities)
    {
        var result = new List<ContentEntity>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var indices = new HashSet<(string EntityType, long LocalIndex)>();

        foreach (var entity in entities)
        {
            if (entity is null)
            {
                throw new InvalidDataException("Entity collection contains null.");
            }

            if (string.IsNullOrWhiteSpace(entity.EntityType) ||
                !IsValidEntityType(entity.EntityType))
            {
                throw new InvalidDataException($"Invalid entity type: '{entity.EntityType}'.");
            }

            if (string.IsNullOrWhiteSpace(entity.ContentKey))
            {
                throw new InvalidDataException($"Entity {entity.EntityType}/{entity.LocalIndex} has no content key.");
            }

            if (entity.SchemaVersion <= 0)
            {
                throw new InvalidDataException(
                    $"Entity {entity.ContentKey} has invalid schema version {entity.SchemaVersion}.");
            }

            var index = (entity.EntityType, entity.LocalIndex);
            if (!indices.Add(index))
            {
                throw new InvalidDataException(
                    $"Duplicate local index {entity.LocalIndex} for entity type '{entity.EntityType}'.");
            }

            if (!keys.Add(entity.ContentKey))
            {
                throw new InvalidDataException($"Duplicate content key '{entity.ContentKey}'.");
            }

            result.Add(entity);
        }

        return result
            .OrderBy(entity => entity.EntityType, StringComparer.Ordinal)
            .ThenBy(entity => entity.LocalIndex)
            .ThenBy(entity => entity.ContentKey, StringComparer.Ordinal)
            .ToList();
    }

    public static string ComputeContentHash(IReadOnlyDictionary<string, ContentSnapshotFile> files)
    {
        var builder = new StringBuilder();
        builder.Append(ContentSnapshotFormat.ContentHashPrefix).Append('\n');
        foreach (var (entityType, file) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(entityType)
                .Append('\t')
                .Append(file.Count)
                .Append('\t')
                .Append(file.Sha256)
                .Append('\n');
        }

        return ContentJson.Sha256Hex(builder.ToString());
    }

    private static bool IsValidEntityType(string entityType)
    {
        if (entityType.Length is 0 or > 64) return false;
        if (entityType[0] is < 'a' or > 'z') return false;
        return entityType.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }
}
