namespace Crystal.Patcher;

public static class PatchPath
{
    private static readonly char[] InvalidWindowsCharacters = { '*', '?', '<', '>', '|', '"' };
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Path is empty.";
            return false;
        }

        var candidate = value.Replace('\\', '/');
        if (candidate.StartsWith('/'))
        {
            error = $"Path '{value}' must be relative.";
            return false;
        }

        var segments = candidate.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0))
        {
            error = $"Path '{value}' contains an empty segment.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                error = $"Path '{value}' contains a traversal segment.";
                return false;
            }

            if (segment.Contains(':') ||
                segment.IndexOfAny(InvalidWindowsCharacters) >= 0 ||
                segment.Any(char.IsControl) ||
                !string.Equals(segment, segment.Trim(), StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal))
            {
                error = $"Path '{value}' contains an invalid character.";
                return false;
            }

            var stem = segment.Split('.')[0];
            if (ReservedWindowsNames.Contains(stem))
            {
                error = $"Path '{value}' uses a reserved Windows device name.";
                return false;
            }
        }

        if (segments[0].Equals(PatchManifestFormat.StateDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Path '{value}' targets the reserved update state directory.";
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    public static string ResolveUnder(string rootDirectory, string relativePath)
    {
        if (!TryNormalize(relativePath, out var normalized, out var error))
        {
            throw new InvalidDataException(error);
        }

        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(
            Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException($"Path '{relativePath}' escapes '{root}'.");
        }

        return candidate;
    }

    public static string NormalizeOrThrow(string value)
    {
        if (!TryNormalize(value, out var normalized, out var error))
        {
            throw new InvalidDataException(error);
        }

        return normalized;
    }
}
