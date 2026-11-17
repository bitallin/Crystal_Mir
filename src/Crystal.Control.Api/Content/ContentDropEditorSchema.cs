using System.Text.Json;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public static class ContentDropEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "drop";

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("monsterIndex", "怪物索引", "reference", required: true, referenceType: "monster", storeAs: "index", defaultValue: 0),
        ContentEditorValidation.Field("monsterName", "怪物名称", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("dropPath", "掉落路径", "string", defaultValue: ""),
        ContentEditorValidation.Field("fileName", "资源文件", "string", readOnly: true),
        ContentEditorValidation.Field("drops", "掉落表", "drop-table", required: true, minItems: 1, defaultValue: Array.Empty<object>())
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "怪物掉落",
        "编辑物品、金币和嵌套掉落组；概率、目标唯一性和物品引用在草稿校验与发布时检查。",
        [
            new ContentEditorSectionDto("target", "掉落目标", "每个怪物只能有一份掉落表。", Fields.Take(4).ToArray()),
            new ContentEditorSectionDto("drops", "掉落条目", "支持物品、金币和嵌套组。", Fields.Skip(4).ToArray())
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
        if (data.TryGetProperty("drops", out var drops) && drops.ValueKind == JsonValueKind.Array)
        {
            ValidateDrops(drops, "drops", errors, 0);
        }

        if (ContentEditorValidation.ReadLong(data, "monsterIndex") is { } monsterIndex && monsterIndex != localIndex)
        {
            errors["monsterIndex"] = [$"monsterIndex must equal localIndex {localIndex}."];
        }

        if (!string.IsNullOrWhiteSpace(contentKey) &&
            !string.Equals(contentKey, $"{EntityType}.monster.{localIndex}", StringComparison.Ordinal))
        {
            errors["contentKey"] = [$"contentKey must be '{EntityType}.monster.{localIndex}'."];
        }

        return errors;
    }

    public static IEnumerable<string> ValidateCompleteness(JsonElement data)
    {
        foreach (var issue in ContentEditorValidation.ValidateRequiredFields(data, Fields))
        {
            yield return issue;
        }

        if (ContentEditorValidation.ReadLong(data, "monsterIndex") is not > 0)
        {
            yield return "Drop table must reference a positive monster index.";
        }

        if (!data.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Array ||
            drops.GetArrayLength() == 0)
        {
            yield return "Drop table must contain at least one entry.";
        }
    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        var monsterIndex = ContentEditorValidation.ReadLong(data, "monsterIndex") ?? nextLocalIndex;
        return new ContentEditorIdentity(monsterIndex, $"{EntityType}.monster.{monsterIndex}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return ContentEditorJson.Normalize(data, node =>
        {
            var monsterName = ContentEditorJson.String(node, "monsterName")?.Trim();
            var dropPath = ContentEditorJson.String(node, "dropPath")?.Trim();
            if (string.IsNullOrWhiteSpace(dropPath))
            {
                dropPath = monsterName;
            }

            if (!string.IsNullOrWhiteSpace(dropPath))
            {
                var relative = dropPath.Replace('\\', '/').Trim('/');
                if (relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    relative = relative[..^4];
                }

                node["dropPath"] = relative.Replace('/', '\\');
                node["fileName"] = $"Envir/Drops/{relative}.txt";
            }
        });
    }

    public static IEnumerable<ContentEditorReferenceValue> GetReferences(JsonElement data)
    {
        if (ContentEditorValidation.ReadLong(data, "monsterIndex") is > 0 and var monsterIndex)
        {
            yield return new ContentEditorReferenceValue("monsterIndex", "monster", monsterIndex);
        }

        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("drops", out var drops) && drops.ValueKind == JsonValueKind.Array)
        {
            foreach (var reference in EnumerateItemReferences(drops, "drops", 0))
            {
                yield return reference;
            }
        }
    }

    private static IEnumerable<ContentEditorReferenceValue> EnumerateItemReferences(
        JsonElement drops,
        string path,
        int depth)
    {
        if (depth > 16 || drops.ValueKind != JsonValueKind.Array) yield break;
        var index = 0;
        foreach (var drop in drops.EnumerateArray())
        {
            var itemPath = $"{path}[{index}].Item";
            if (drop.ValueKind == JsonValueKind.Object)
            {
                if (drop.TryGetProperty("Item", out var item) &&
                    item.ValueKind == JsonValueKind.Object &&
                    ContentEditorValidation.ReadLong(item, "index") is > 0 and var itemIndex)
                {
                    yield return new ContentEditorReferenceValue(itemPath, "item", itemIndex);
                }

                if (TryGetGroupItems(drop, out var groupItems))
                {
                    foreach (var reference in EnumerateItemReferences(
                                 groupItems,
                                 $"{path}[{index}].GroupedDrop.items",
                                 depth + 1))
                    {
                        yield return reference;
                    }
                }
            }

            index++;
        }
    }

    private static void ValidateDrops(
        JsonElement drops,
        string path,
        IDictionary<string, string[]> errors,
        int depth)
    {
        if (depth > 16)
        {
            errors[path] = [$"{path} exceeds the maximum nested drop depth."];
            return;
        }

        var index = 0;
        foreach (var drop in drops.EnumerateArray())
        {
            var dropPath = $"{path}[{index}]";
            if (drop.ValueKind != JsonValueKind.Object)
            {
                errors[dropPath] = [$"{dropPath} must be an object."];
                index++;
                continue;
            }

            if (ContentEditorValidation.ReadLong(drop, "Chance") is not long chance || chance < 0)
            {
                errors[$"{dropPath}.Chance"] = [$"{dropPath}.Chance must be a non-negative integer."];
            }

            var hasItem = drop.TryGetProperty("Item", out var item) && item.ValueKind == JsonValueKind.Object;
            var gold = ContentEditorValidation.ReadLong(drop, "Gold") ?? 0;
            var hasGroup = TryGetGroupItems(drop, out var groupItems);
            if ((hasItem ? 1 : 0) + (gold > 0 ? 1 : 0) + (hasGroup ? 1 : 0) != 1)
            {
                errors[dropPath] = [$"{dropPath} must select exactly one of Item, Gold or GroupedDrop."];
            }

            if (hasItem)
            {
                ContentEditorValidation.ValidateValue(
                    ContentEditorValidation.Field("Item", "物品", "reference", required: true, referenceType: "item", storeAs: "ref"),
                    item,
                    $"{dropPath}.Item",
                    errors);
            }

            if (hasGroup)
            {
                ValidateDrops(groupItems, $"{dropPath}.GroupedDrop.items", errors, depth + 1);
            }

            index++;
        }
    }

    private static bool TryGetGroupItems(JsonElement drop, out JsonElement items)
    {
        items = default;
        if (!drop.TryGetProperty("GroupedDrop", out var group) &&
            !drop.TryGetProperty("groupedDrop", out group))
        {
            return false;
        }

        if (group.ValueKind == JsonValueKind.Array)
        {
            items = group;
            return true;
        }

        return group.ValueKind == JsonValueKind.Object &&
               group.TryGetProperty("items", out items) &&
               items.ValueKind == JsonValueKind.Array;
    }
}
