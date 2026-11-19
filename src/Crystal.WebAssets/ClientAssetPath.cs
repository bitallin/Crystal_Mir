namespace Crystal.WebAssets;

public static class ClientAssetPath
{
    public static string? ResolveDataFile(string clientRoot, string relativePath)
    {
        return ResolveFile(Path.Combine(clientRoot, "Data"), relativePath);
    }

    public static string? ResolveMapFile(string clientRoot, string relativePath)
    {
        return ResolveFile(Path.Combine(clientRoot, "Map"), relativePath);
    }

    public static string? ResolveFile(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;

        string rootPath = Path.GetFullPath(root);
        string[] segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or "..")) return null;

        string current = rootPath;
        for (int index = 0; index < segments.Length; index++)
        {
            bool isFile = index == segments.Length - 1;
            string? match = FindEntry(current, segments[index], isFile);
            if (match is null) return null;
            current = match;
        }

        return current;
    }

    private static string? FindEntry(string directory, string name, bool file)
    {
        if (!Directory.Exists(directory)) return null;

        string exact = Path.Combine(directory, name);
        if ((file ? File.Exists(exact) : Directory.Exists(exact))) return exact;

        List<string> matches = Directory.EnumerateFileSystemEntries(directory)
            .Where(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
            .Where(path => file ? File.Exists(path) : Directory.Exists(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();

        if (matches.Count > 1)
            throw new InvalidDataException($"Client asset path is ambiguous: {Path.Combine(directory, name)}");

        return matches.Count == 1 ? matches[0] : null;
    }
}
