using System.Globalization;
using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentEditorValidation
{
    public static IReadOnlyList<ContentEditorOptionDto> Options(params (string Value, string Label)[] values)
    {
        return values.Select(value => new ContentEditorOptionDto(value.Value, value.Label)).ToArray();
    }

    public static ContentEditorFieldDto Field(
        string path,
        string label,
        string kind,
        bool required = false,
        bool readOnly = false,
        object? defaultValue = null,
        long? min = null,
        long? max = null,
        IReadOnlyList<ContentEditorOptionDto>? options = null,
        IReadOnlyList<ContentEditorFieldDto>? fields = null,
        string? referenceType = null,
        string? storeAs = null,
        bool nullable = false,
        bool multiline = false,
        int? minItems = null,
        int? maxItems = null)
    {
        return new ContentEditorFieldDto(
            path,
            label,
            kind,
            required,
            readOnly,
            defaultValue,
            min,
            max,
            options,
            fields,
            referenceType,
            storeAs,
            nullable,
            multiline,
            minItems,
            maxItems);
    }

    public static IEnumerable<ContentEditorFieldDto> AllFields(ContentEditorSchemaDto schema)
    {
        foreach (var field in schema.Sections.SelectMany(section => section.Fields))
        {
            yield return field;
        }
    }

    public static void ValidateFields(
        JsonElement data,
        IEnumerable<ContentEditorFieldDto> fields,
        IDictionary<string, string[]> errors,
        string pathPrefix = "")
    {
        if (data.ValueKind != JsonValueKind.Object) return;

        foreach (var field in fields)
        {
            if (field.ReadOnly) continue;

            var fullPath = Combine(pathPrefix, field.Path);
            if (!TryGetPath(data, field.Path, out var value) || value.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            ValidateValue(field, value, fullPath, errors);
        }
    }

    public static IEnumerable<string> ValidateRequiredFields(
        JsonElement data,
        IEnumerable<ContentEditorFieldDto> fields,
        string pathPrefix = "")
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            yield return "Entity data must be a JSON object.";
            yield break;
        }

        foreach (var field in fields)
        {
            if (!field.Required || field.ReadOnly) continue;
            if (!TryGetPath(data, field.Path, out var value) || value.ValueKind == JsonValueKind.Undefined)
            {
                yield return $"Missing required field '{Combine(pathPrefix, field.Path)}'.";
            }
        }
    }

    public static void ValidateValue(
        ContentEditorFieldDto field,
        JsonElement value,
        string fullPath,
        IDictionary<string, string[]> errors)
    {
        if (field.Nullable == true && value.ValueKind == JsonValueKind.Null) return;

        switch (field.Kind)
        {
            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer))
                {
                    errors[fullPath] = [$"{fullPath} must be an integer."];
                    return;
                }

                if ((field.Min is not null && integer < field.Min.Value) ||
                    (field.Max is not null && integer > field.Max.Value))
                {
                    errors[fullPath] =
                    [
                        $"{fullPath} must be between {field.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} and {field.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}."
                    ];
                }

                return;
            case "number":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
                {
                    errors[fullPath] = [$"{fullPath} must be a number."];
                    return;
                }

                if ((field.Min is not null && number < field.Min.Value) ||
                    (field.Max is not null && number > field.Max.Value))
                {
                    errors[fullPath] =
                    [
                        $"{fullPath} must be between {field.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} and {field.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}."
                    ];
                }

                return;
            case "string":
            case "code":
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors[fullPath] = [$"{fullPath} must be a string."];
                }

                return;
            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors[fullPath] = [$"{fullPath} must be a boolean."];
                }

                return;
            case "enum":
                ValidateEnum(field, value, fullPath, errors);
                return;
            case "flags":
                ValidateFlags(field, value, fullPath, errors);
                return;
            case "reference":
                ValidateReference(field, value, fullPath, errors);
                return;
            case "color":
                ValidateColor(value, fullPath, errors);
                return;
            case "object":
                if (value.ValueKind != JsonValueKind.Object)
                {
                    errors[fullPath] = [$"{fullPath} must be an object."];
                    return;
                }

                ValidateFields(value, field.Fields ?? [], errors, fullPath);
                return;
            case "array":
                ValidateArray(field, value, fullPath, errors);
                return;
            case "string-array":
                ValidatePrimitiveArray(value, fullPath, JsonValueKind.String, errors);
                return;
            case "integer-array":
                ValidatePrimitiveArray(value, fullPath, JsonValueKind.Number, errors);
                return;
            case "reference-array":
                ValidateReferenceArray(field, value, fullPath, errors);
                return;
            case "stat-map":
                ValidateStatMap(value, fullPath, errors);
                return;
            case "readonly":
            case "drop-table":
                return;
        }
    }

    public static bool TryGetPath(JsonElement data, string path, out JsonElement value)
    {
        value = data;
        if (string.IsNullOrWhiteSpace(path)) return true;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    public static string? ReadString(JsonElement data, string name)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static long? ReadLong(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    }

    public static bool? ReadBool(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public static string Combine(string prefix, string path)
    {
        return string.IsNullOrWhiteSpace(prefix) ? path : $"{prefix}.{path}";
    }

    private static void ValidateArray(
        ContentEditorFieldDto field,
        JsonElement value,
        string fullPath,
        IDictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors[fullPath] = [$"{fullPath} must be an array."];
            return;
        }

        var count = value.GetArrayLength();
        if (field.MinItems is not null && count < field.MinItems.Value)
        {
            errors[fullPath] = [$"{fullPath} must contain at least {field.MinItems.Value} item(s)."];
            return;
        }

        if (field.MaxItems is not null && count > field.MaxItems.Value)
        {
            errors[fullPath] = [$"{fullPath} must contain at most {field.MaxItems.Value} item(s)."];
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateValue(
                new ContentEditorFieldDto(field.Path, field.Label, field.Kind == "array" ? "object" : field.Kind,
                    Fields: field.Fields),
                item,
                $"{fullPath}[{index}]",
                errors);
            index++;
        }
    }

    private static void ValidateStatMap(JsonElement value, string fullPath, IDictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors[fullPath] = [$"{fullPath} must be an object."];
            return;
        }

        if (!value.TryGetProperty("Values", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in values.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out _))
            {
                errors[$"{fullPath}.Values.{property.Name}"] = ["Stat values must be 32-bit integers."];
            }
        }
    }

    private static void ValidatePrimitiveArray(
        JsonElement value,
        string fullPath,
        JsonValueKind kind,
        IDictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors[fullPath] = [$"{fullPath} must be an array."];
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != kind ||
                (kind == JsonValueKind.Number && !item.TryGetInt64(out _)))
            {
                errors[$"{fullPath}[{index}]"] = [$"{fullPath}[{index}] has an invalid type."];
            }

            index++;
        }
    }

    private static void ValidateReferenceArray(
        ContentEditorFieldDto field,
        JsonElement value,
        string fullPath,
        IDictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors[fullPath] = [$"{fullPath} must be an array."];
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateReference(field, item, $"{fullPath}[{index}]", errors);
            index++;
        }
    }

    private static void ValidateEnum(
        ContentEditorFieldDto field,
        JsonElement value,
        string fullPath,
        IDictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors[fullPath] = [$"{fullPath} must be a string."];
            return;
        }

        var values = (field.Options ?? []).Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!values.Contains(value.GetString() ?? string.Empty))
        {
            errors[fullPath] = [$"{fullPath} must be one of the known values."];
        }
    }

    private static void ValidateFlags(
        ContentEditorFieldDto field,
        JsonElement value,
        string fullPath,
        IDictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors[fullPath] = [$"{fullPath} must be a comma-separated string."];
            return;
        }

        var allowed = (field.Options ?? []).Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var flag in (value.GetString() ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allowed.Count > 0 && !allowed.Contains(flag))
            {
                errors[fullPath] = [$"{fullPath} contains unknown flag '{flag}'."];
                return;
            }
        }
    }

    private static void ValidateReference(
        ContentEditorFieldDto field,
        JsonElement value,
        string fullPath,
        IDictionary<string, string[]> errors)
    {
        if (string.Equals(field.StoreAs, "string", StringComparison.Ordinal))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                errors[fullPath] = [$"{fullPath} must be a non-empty reference key."];
            }

            return;
        }

        if (string.Equals(field.StoreAs, "ref", StringComparison.Ordinal) || value.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                errors[fullPath] = [$"{fullPath} must be a reference object."];
                return;
            }

            var type = ReadString(value, "$ref");
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(field.ReferenceType) &&
                !string.Equals(type, field.ReferenceType, StringComparison.Ordinal))
            {
                errors[fullPath] = [$"{fullPath} must reference '{field.ReferenceType}'."];
                return;
            }

            if (ReadLong(value, "index") is not long index || index < 0)
            {
                errors[fullPath] = [$"{fullPath}.index must be a non-negative integer."];
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var numeric) || numeric < 0)
        {
            errors[fullPath] = [$"{fullPath} must be a non-negative integer reference index."];
        }
    }

    private static void ValidateColor(JsonElement value, string fullPath, IDictionary<string, string[]> errors)
    {
        if (value.ValueKind == JsonValueKind.String) return;
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors[fullPath] = [$"{fullPath} must be a color object or hex string."];
            return;
        }

        foreach (var channel in new[] { "a", "r", "g", "b" })
        {
            if (!value.TryGetProperty(channel, out var component) ||
                component.ValueKind != JsonValueKind.Number ||
                !component.TryGetInt32(out var numeric) ||
                numeric is < 0 or > 255)
            {
                errors[$"{fullPath}.{channel}"] = [$"{fullPath}.{channel} must be between 0 and 255."];
            }
        }
    }
}
