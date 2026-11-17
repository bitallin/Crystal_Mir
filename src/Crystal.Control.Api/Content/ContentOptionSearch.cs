using System.Text.Json;

namespace Crystal.Control.Api.Content;

internal static class ContentOptionSearch
{
    private static readonly string[] FallbackLabelPaths = ["Name", "Title", "fileName", "monsterName", "name"];

    public static string LabelExpression(string alias, string entityType)
    {
        var paths = new List<string>();
        if (ContentEditorSchemas.TryGet(entityType, out var schema) &&
            !string.IsNullOrWhiteSpace(schema.LabelPath))
        {
            paths.Add(schema.LabelPath);
        }

        paths.AddRange(FallbackLabelPaths);
        var parts = paths.Distinct(StringComparer.Ordinal)
            .Select(path => $"NULLIF({alias}.data->>'{EscapePath(path)}', '')")
            .Append($"{alias}.content_key");
        return $"COALESCE({string.Join(", ", parts)})";
    }

    public static string SearchExpression(string alias, string entityType)
    {
        var paths = new List<string> { "Name", "Title", "FileName", "fileName", "monsterName", "name" };
        if (ContentEditorSchemas.TryGet(entityType, out var schema) && schema.SearchPaths is not null)
        {
            paths.AddRange(schema.SearchPaths);
        }

        var parts = paths.Distinct(StringComparer.Ordinal)
            .Select(path => $"COALESCE({alias}.data->>'{EscapePath(path)}', '')")
            .Append($"{alias}.content_key")
            .Append($"{alias}.local_index::text");
        return $"lower(concat_ws(' ', {string.Join(", ", parts)}))";
    }

    public static string Label(JsonElement data, string contentKey, string entityType)
    {
        if (ContentEditorSchemas.TryGet(entityType, out var schema) &&
            !string.IsNullOrWhiteSpace(schema.LabelPath) &&
            data.TryGetProperty(schema.LabelPath, out var label) &&
            label.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(label.GetString()))
        {
            return label.GetString()!;
        }

        foreach (var path in FallbackLabelPaths)
        {
            if (data.TryGetProperty(path, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }

        return contentKey;
    }

    public static string? Summary(JsonElement data)
    {
        foreach (var path in new[] { "Title", "FileName", "relativePath", "dropPath", "Group", "Category", "Name", "monsterName" })
        {
            if (data.TryGetProperty(path, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string EscapePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (path.Any(character => !char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.')))
        {
            throw new ArgumentException($"Invalid JSON field path '{path}'.", nameof(path));
        }

        return path.Replace("'", "''", StringComparison.Ordinal);
    }
}
