using System.Text.Json;

namespace Crystal.Content;

public static class ContentSnapshotReader
{
    public static ContentSnapshot Read(string snapshotDirectory)
    {
        var directory = Path.GetFullPath(snapshotDirectory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Snapshot manifest not found.", manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<ContentSnapshotManifest>(
            File.ReadAllText(manifestPath),
            ContentJson.IndentedOptions)
            ?? throw new InvalidDataException($"Cannot read snapshot manifest: {manifestPath}");

        if (manifest.FormatVersion != ContentSnapshotFormat.FormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported snapshot format {manifest.FormatVersion}; expected {ContentSnapshotFormat.FormatVersion}.");
        }

        var entities = new List<ContentEntity>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entityType, file) in manifest.Entities)
        {
            var path = Path.Combine(directory, file.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Snapshot entity file not found: {file.File}", path);
            }

            var bytes = File.ReadAllBytes(path);
            var actualHash = ContentJson.Sha256Hex(bytes);
            if (!string.Equals(actualHash, file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Snapshot entity hash mismatch for {file.File}: manifest={file.Sha256}, actual={actualHash}.");
            }

            if (bytes.LongLength != file.SizeBytes)
            {
                throw new InvalidDataException(
                    $"Snapshot entity size mismatch for {file.File}: manifest={file.SizeBytes}, actual={bytes.LongLength}.");
            }

            var count = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entity = JsonSerializer.Deserialize<ContentEntity>(line, ContentJson.CompactOptions)
                    ?? throw new InvalidDataException($"Invalid entity line in {file.File}.");
                if (!string.Equals(entity.EntityType, entityType, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Entity type mismatch in {file.File}: expected '{entityType}', got '{entity.EntityType}'.");
                }

                if (!seenKeys.Add(entity.ContentKey))
                {
                    throw new InvalidDataException($"Duplicate content key '{entity.ContentKey}'.");
                }

                entities.Add(entity);
                count++;
            }

            if (count != file.Count)
            {
                throw new InvalidDataException(
                    $"Entity count mismatch in {file.File}: manifest={file.Count}, file={count}.");
            }
        }

        var actualContentHash = ContentSnapshotWriter.ComputeContentHash(manifest.Entities);
        if (!string.Equals(actualContentHash, manifest.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Snapshot content hash mismatch: manifest={manifest.ContentHash}, actual={actualContentHash}.");
        }

        return new ContentSnapshot { Manifest = manifest, Entities = entities };
    }
}
