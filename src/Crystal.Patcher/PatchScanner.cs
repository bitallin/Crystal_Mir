namespace Crystal.Patcher;

public sealed record LocalPatchFile(string Path, long Size, string Sha256);

public sealed class PatchScanOptions
{
    public List<string> ExcludedPrefixes { get; init; } = new();
}

public static class PatchScanner
{
    public static IReadOnlyDictionary<string, LocalPatchFile> Scan(
        string rootDirectory,
        PatchScanOptions? options = null)
    {
        options ??= new PatchScanOptions();
        var root = Path.GetFullPath(rootDirectory);
        var files = new Dictionary<string, LocalPatchFile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return files;
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };

        foreach (var fullPath in Directory.EnumerateFiles(root, "*", enumeration))
        {
            var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            if (relative.Equals(PatchManifestFormat.StateDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(PatchManifestFormat.StateDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (options.ExcludedPrefixes.Any(prefix =>
                    relative.StartsWith(prefix.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals(prefix.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var info = new FileInfo(fullPath);
            files[relative] = new LocalPatchFile(relative, info.Length, PatchJson.Sha256File(fullPath));
        }

        return files;
    }
}
