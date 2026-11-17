using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentRecipeEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "recipe";

    private static readonly IReadOnlyList<ContentEditorFieldDto> RecipeItemFields =
    [
        ContentEditorValidation.Field("itemIndex", "物品索引", "reference", required: true, referenceType: "item", storeAs: "index", defaultValue: 0),
        ContentEditorValidation.Field("itemName", "物品名称", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("count", "数量", "integer", required: true, defaultValue: 1, min: 1, max: ushort.MaxValue),
        ContentEditorValidation.Field("currentDura", "当前持久", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("maxDura", "最大持久", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue)
    ];

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("name", "配方面板名称", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("fileName", "配方文件", "string", readOnly: true),
        ContentEditorValidation.Field("output", "产出物品", "object", required: true, fields: RecipeItemFields),
        ContentEditorValidation.Field("ingredients", "材料", "array", required: true, fields: RecipeItemFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("tools", "工具", "array", required: true, fields: RecipeItemFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("requiredLevel", "需求等级", "integer", min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("requiredGender", "需求性别", "string", nullable: true),
        ContentEditorValidation.Field("requiredClasses", "需求职业", "string-array", required: true, defaultValue: Array.Empty<string>()),
        ContentEditorValidation.Field("requiredFlags", "需求标记", "integer-array", required: true, defaultValue: Array.Empty<int>()),
        ContentEditorValidation.Field("requiredQuests", "前置任务", "reference-array", required: true, referenceType: "quest", storeAs: "index", defaultValue: Array.Empty<int>()),
        ContentEditorValidation.Field("chance", "成功率", "integer", required: true, defaultValue: 100, min: 0, max: 100),
        ContentEditorValidation.Field("gold", "金币费用", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue)
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "合成配方",
        "编辑产出、材料、工具、条件、成功率和金币费用；配方文件身份与产出物品名保持一致。",
        [
            new ContentEditorSectionDto("result", "产出与材料", "配方名称、产出、材料和工具。", Fields.Take(5).ToArray()),
            new ContentEditorSectionDto("requirements", "合成条件", "等级、性别、职业、标记、前置任务和费用。", Fields.Skip(5).ToArray())
        ]);

    public static Dictionary<string, string[]> ValidateStructure(JsonElement data, long localIndex, string? contentKey)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            errors["data"] = ["data must be a JSON object."];
            return errors;
        }

        ContentEditorValidation.ValidateFields(data, Fields, errors);
        if (!string.IsNullOrWhiteSpace(contentKey))
        {
            var expected = ResolveIdentity(data, localIndex).ContentKey;
            var legacy = LegacyContentKey(data, localIndex);
            if (!string.Equals(contentKey, expected, StringComparison.Ordinal) &&
                !string.Equals(contentKey, legacy, StringComparison.Ordinal))
            {
                errors["contentKey"] = [$"contentKey must be '{expected}'."];
            }
        }

        return errors;
    }

    public static IEnumerable<string> ValidateCompleteness(JsonElement data)
    {
        foreach (var issue in ContentEditorValidation.ValidateRequiredFields(data, Fields))
        {
            yield return issue;
        }

        if (!TryReadOutput(data, out var output))
        {
            yield return "Recipe output must be an object.";
            yield break;
        }

        var itemIndex = ContentEditorValidation.ReadLong(output, "itemIndex") ?? 0;
        var itemName = ContentEditorValidation.ReadString(output, "itemName");
        if (itemIndex <= 0)
        {
            yield return "Recipe must select a positive output item index.";
        }

        if (string.IsNullOrWhiteSpace(itemName))
        {
            yield return "Recipe output item name must not be empty.";
        }

        if (ContentEditorValidation.ReadString(data, "name") is { } name && string.IsNullOrWhiteSpace(name))
        {
            yield return "Recipe name must not be empty.";
        }
    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        var name = TryReadOutput(data, out var output)
            ? ContentEditorValidation.ReadString(output, "itemName")
              ?? ContentEditorValidation.ReadString(data, "name")
              ?? nextLocalIndex.ToString()
            : ContentEditorValidation.ReadString(data, "name") ?? nextLocalIndex.ToString();
        var segment = ContentEditorJson.SafeKeySegment(name, nextLocalIndex.ToString());
        return new ContentEditorIdentity(nextLocalIndex, $"{EntityType}.{segment}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return ContentEditorJson.Normalize(data, node =>
        {
            var name = ContentEditorJson.String(node, "name")?.Trim();
            var output = ContentEditorJson.Object(node, "output");
            if (output is not null && string.IsNullOrWhiteSpace(ContentEditorJson.String(output, "itemName")))
            {
                output["itemName"] = name;
            }

            if (string.IsNullOrWhiteSpace(name) && ideaName(output) is { Length: > 0 } outputName)
            {
                node["name"] = outputName;
                name = outputName;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                node["fileName"] = $"Envir/Recipe/{name}.txt";
            }
        });
    }

    public static IEnumerable<ContentEditorReferenceValue> GetReferences(JsonElement data)
    {
        if (TryReadOutput(data, out var output))
        {
            foreach (var reference in ReadReference(output, "output.itemIndex", "item"))
            {
                yield return reference;
            }
        }

        foreach (var path in new[] { "ingredients", "tools" })
        {
            if (!data.TryGetProperty(path, out var items) || items.ValueKind != JsonValueKind.Array) continue;
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                foreach (var reference in ReadReference(item, $"{path}[{index}].itemIndex", "item"))
                {
                    yield return reference;
                }

                index++;
            }
        }

        if (data.TryGetProperty("requiredQuests", out var quests) && quests.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var quest in quests.EnumerateArray())
            {
                if (quest.TryGetInt64(out var questIndex) && questIndex > 0)
                {
                    yield return new ContentEditorReferenceValue($"requiredQuests[{index}]", "quest", questIndex);
                }

                index++;
            }
        }
    }

    private static IEnumerable<ContentEditorReferenceValue> ReadReference(
        JsonElement item,
        string path,
        string entityType)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            ContentEditorValidation.ReadLong(item, "itemIndex") is > 0 and var index)
        {
            yield return new ContentEditorReferenceValue(path, entityType, index);
        }
    }

    private static bool TryReadOutput(JsonElement data, out JsonElement output)
    {
        output = default;
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty("output", out output) &&
               output.ValueKind == JsonValueKind.Object;
    }

    private static string LegacyContentKey(JsonElement data, long localIndex)
    {
        var name = TryReadOutput(data, out var output)
            ? ContentEditorValidation.ReadString(output, "itemName")
              ?? ContentEditorValidation.ReadString(data, "name")
              ?? localIndex.ToString()
            : ContentEditorValidation.ReadString(data, "name") ?? localIndex.ToString();
        return $"{EntityType}.{name}";
    }

    private static string? ideaName(System.Text.Json.Nodes.JsonObject? output)
    {
        return output is null ? null : ContentEditorJson.String(output, "itemName");
    }
}
