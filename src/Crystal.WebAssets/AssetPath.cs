using System.Text;

namespace Crystal.WebAssets;

public static class AssetPath
{
    public static string SafeSegment(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') builder.Append(character);
            else builder.Append('-');
        }

        string result = builder.ToString().Trim('-', '.');
        return string.IsNullOrWhiteSpace(result) ? "asset" : result;
    }

    public static string NormalizeRelative(string value)
    {
        string normalized = value.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(static part => part is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe relative asset path '{value}'.");
        return normalized;
    }
}
