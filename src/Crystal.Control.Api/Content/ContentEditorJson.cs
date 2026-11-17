using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

internal static class ContentEditorJson
{
    private static readonly Regex UnsafeKeyCharacters = new(
        "[^A-Za-z0-9._-]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static JsonElement Normalize(JsonElement data, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(data.GetRawText()) as JsonObject
                   ?? throw new JsonException("Entity data must be a JSON object.");
        mutate(node);
        return JsonSerializer.SerializeToElement(node, ContentJson.CompactOptions);
    }

    public static void Set(JsonObject data, string property, JsonNode? value)
    {
        data[property] = value;
    }

    public static void SetString(JsonObject data, string property, string? value)
    {
        data[property] = value;
    }

    public static void SetNumber<T>(JsonObject data, string property, T value) where T : struct, IConvertible
    {
        data[property] = JsonValue.Create(value);
    }

    public static void SetBoolean(JsonObject data, string property, bool value)
    {
        data[property] = JsonValue.Create(value);
    }

    public static string? String(JsonObject data, string property)
    {
        return data[property] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : null;
    }

    public static long? Long(JsonObject data, string property)
    {
        if (data[property] is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var result)) return result;
        if (value.TryGetValue<int>(out var intResult)) return intResult;
        if (value.TryGetValue<string>(out var text) && long.TryParse(text, out var parsed)) return parsed;
        return null;
    }

    public static bool? Boolean(JsonObject data, string property)
    {
        return data[property] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;
    }

    public static JsonObject? Object(JsonObject data, string property)
    {
        return data[property] as JsonObject;
    }

    public static JsonArray? Array(JsonObject data, string property)
    {
        return data[property] as JsonArray;
    }

    public static JsonObject Reference(string entityType, long index, string? key = null)
    {
        var reference = new JsonObject
        {
            ["$ref"] = entityType,
            ["index"] = index
        };
        if (!string.IsNullOrWhiteSpace(key))
        {
            reference["key"] = key;
        }

        return reference;
    }

    public static string SafeKeySegment(string value, string fallback)
    {
        var normalized = value.Trim()
            .Replace('\\', '.')
            .Replace('/', '.');
        normalized = UnsafeKeyCharacters.Replace(normalized, "-").Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    public static string NormalizeScriptPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        const string prefix = "Envir/NPCs/";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
        }
        else if (normalized.StartsWith("NPCs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["NPCs/".Length..];
        }

        if (normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.Trim('/');
    }
}
