using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentQuestEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "quest";

    private static readonly IReadOnlyList<ContentEditorOptionDto> QuestTypeOptions =
        ContentEditorValidation.Options(
            ("General", "普通"),
            ("Daily", "每日"),
            ("Repeatable", "可重复"),
            ("Story", "主线"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> RequiredClassOptions =
        ContentEditorValidation.Options(
            ("None", "无限制"),
            ("Warrior", "战士"),
            ("Wizard", "法师"),
            ("Taoist", "道士"),
            ("Assassin", "刺客"),
            ("Archer", "弓箭手"),
            ("WarWizTao", "战法道"));

    private static readonly IReadOnlyList<ContentEditorFieldDto> ItemTaskFields =
    [
        ContentEditorValidation.Field("Item", "物品", "reference", required: true, referenceType: "item", storeAs: "ref"),
        ContentEditorValidation.Field("Count", "数量", "integer", required: true, defaultValue: 1, min: 1, max: ushort.MaxValue),
        ContentEditorValidation.Field("Message", "提示", "string", defaultValue: "")
    ];

    private static readonly IReadOnlyList<ContentEditorFieldDto> KillTaskFields =
    [
        ContentEditorValidation.Field("Monster", "怪物", "reference", required: true, referenceType: "monster", storeAs: "ref"),
        ContentEditorValidation.Field("Count", "数量", "integer", required: true, defaultValue: 1, min: 1, max: int.MaxValue),
        ContentEditorValidation.Field("Message", "提示", "string", defaultValue: "")
    ];

    private static readonly IReadOnlyList<ContentEditorFieldDto> FlagTaskFields =
    [
        ContentEditorValidation.Field("Number", "标记", "integer", required: true, defaultValue: 0),
        ContentEditorValidation.Field("Message", "提示", "string", defaultValue: "")
    ];

    private static readonly IReadOnlyList<ContentEditorFieldDto> RewardFields =
    [
        ContentEditorValidation.Field("Item", "物品", "reference", required: true, referenceType: "item", storeAs: "ref"),
        ContentEditorValidation.Field("Count", "数量", "integer", required: true, defaultValue: 1, min: 1, max: ushort.MaxValue)
    ];

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("Index", "本地索引", "readonly", required: true, readOnly: true, defaultValue: 0),
        ContentEditorValidation.Field("Name", "任务名称", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("Group", "任务分组", "string", defaultValue: ""),
        ContentEditorValidation.Field("FileName", "任务文件", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("Type", "任务类型", "enum", required: true, options: QuestTypeOptions, defaultValue: "General"),
        ContentEditorValidation.Field("NpcIndex", "发布 NPC", "reference", required: true, referenceType: "npc", storeAs: "index", defaultValue: 0),
        ContentEditorValidation.Field("FinishNpcIndex", "交付 NPC", "reference", required: true, referenceType: "npc", storeAs: "index", defaultValue: 0),
        ContentEditorValidation.Field("RequiredMinLevel", "最低等级", "integer", required: true, defaultValue: 1, min: 0, max: short.MaxValue),
        ContentEditorValidation.Field("RequiredMaxLevel", "最高等级", "integer", required: true, defaultValue: ushort.MaxValue, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("RequiredQuest", "前置任务", "reference", referenceType: "quest", storeAs: "index", nullable: true, defaultValue: 0),
        ContentEditorValidation.Field("RequiredClass", "职业限制", "enum", required: true, options: RequiredClassOptions, defaultValue: "None"),
        ContentEditorValidation.Field("TimeLimitInSeconds", "时限（秒）", "integer", required: true, defaultValue: 0, min: 0),
        ContentEditorValidation.Field("GotoMessage", "前往提示", "string", defaultValue: ""),
        ContentEditorValidation.Field("KillMessage", "击杀提示", "string", defaultValue: ""),
        ContentEditorValidation.Field("ItemMessage", "物品提示", "string", defaultValue: ""),
        ContentEditorValidation.Field("FlagMessage", "标记提示", "string", defaultValue: ""),
        ContentEditorValidation.Field("Description", "任务描述", "string-array", required: true, defaultValue: Array.Empty<string>()),
        ContentEditorValidation.Field("TaskDescription", "目标描述", "string-array", required: true, defaultValue: Array.Empty<string>()),
        ContentEditorValidation.Field("ReturnDescription", "返回描述", "string-array", required: true, defaultValue: Array.Empty<string>()),
        ContentEditorValidation.Field("CompletionDescription", "完成描述", "string-array", required: true, defaultValue: Array.Empty<string>()),
        ContentEditorValidation.Field("CarryItems", "携带物品", "array", required: true, fields: ItemTaskFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("KillTasks", "击杀目标", "array", required: true, fields: KillTaskFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("ItemTasks", "收集目标", "array", required: true, fields: ItemTaskFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("FlagTasks", "标记目标", "array", required: true, fields: FlagTaskFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("FixedRewards", "固定奖励", "array", required: true, fields: RewardFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("SelectRewards", "可选奖励", "array", required: true, fields: RewardFields, defaultValue: Array.Empty<object>()),
        ContentEditorValidation.Field("GoldReward", "金币奖励", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue),
        ContentEditorValidation.Field("ExpReward", "经验奖励", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue),
        ContentEditorValidation.Field("CreditReward", "元宝奖励", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue)
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "任务定义",
        "编辑任务身份、NPC、等级/职业条件、描述、物品/击杀/标记目标及固定/可选奖励。",
        [
            new ContentEditorSectionDto("identity", "身份与 NPC", "任务身份、类型、发布和交付 NPC。", Fields.Take(7).ToArray()),
            new ContentEditorSectionDto("requirements", "接取条件", "等级、前置任务、职业和时限。", Fields.Skip(7).Take(5).ToArray()),
            new ContentEditorSectionDto("messages", "消息", "各阶段提示文本。", Fields.Skip(12).Take(4).ToArray()),
            new ContentEditorSectionDto("descriptions", "描述", "任务各阶段描述文本。", Fields.Skip(16).Take(4).ToArray()),
            new ContentEditorSectionDto("tasks", "任务目标", "携带、击杀、收集和标记目标。", Fields.Skip(20).Take(4).ToArray()),
            new ContentEditorSectionDto("rewards", "奖励", "固定/可选物品及货币奖励。", Fields.Skip(24).ToArray())
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
        if (data.TryGetProperty("Index", out var index) &&
            (!index.TryGetInt64(out var value) || value != localIndex))
        {
            errors["Index"] = [$"Index must match localIndex {localIndex}."];
        }

        if (!string.IsNullOrWhiteSpace(contentKey) &&
            !string.Equals(contentKey, $"{EntityType}.{localIndex}", StringComparison.Ordinal))
        {
            errors["contentKey"] = [$"contentKey must be '{EntityType}.{localIndex}'."];
        }

        return errors;
    }

    public static IEnumerable<string> ValidateCompleteness(JsonElement data)
    {
        foreach (var issue in ContentEditorValidation.ValidateRequiredFields(data, Fields))
        {
            yield return issue;
        }

        if (ContentEditorValidation.ReadString(data, "Name") is { } name && string.IsNullOrWhiteSpace(name))
        {
            yield return "Quest name must not be empty.";
        }

        var minLevel = ContentEditorValidation.ReadLong(data, "RequiredMinLevel") ?? 0;
        var maxLevel = ContentEditorValidation.ReadLong(data, "RequiredMaxLevel") ?? 0;
        if (maxLevel > 0 && minLevel > maxLevel)
        {
            yield return "Quest minimum level must not exceed maximum level.";
        }
    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        return new ContentEditorIdentity(nextLocalIndex, $"{EntityType}.{nextLocalIndex}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return ContentEditorJson.Normalize(data, node =>
        {
            var npcIndex = ContentEditorJson.Long(node, "NpcIndex") ?? 0;
            var finishNpcIndex = ContentEditorJson.Long(node, "FinishNpcIndex") ?? 0;
            if (finishNpcIndex == 0 && npcIndex > 0)
            {
                node["FinishNpcIndex"] = npcIndex;
            }
        });
    }

    public static IEnumerable<ContentEditorReferenceValue> GetReferences(JsonElement data)
    {
        if (ContentEditorValidation.ReadLong(data, "NpcIndex") is > 0 and var npcIndex)
        {
            yield return new ContentEditorReferenceValue("NpcIndex", "npc", npcIndex);
        }

        if (ContentEditorValidation.ReadLong(data, "FinishNpcIndex") is > 0 and var finishNpcIndex)
        {
            yield return new ContentEditorReferenceValue("FinishNpcIndex", "npc", finishNpcIndex);
        }

        if (ContentEditorValidation.ReadLong(data, "RequiredQuest") is > 0 and < byte.MaxValue and var requiredQuest)
        {
            yield return new ContentEditorReferenceValue("RequiredQuest", "quest", requiredQuest);
        }

        foreach (var path in new[] { "CarryItems", "ItemTasks" })
        {
            foreach (var reference in ReadArrayReferences(data, path, "Item", "item"))
            {
                yield return reference;
            }
        }

        foreach (var reference in ReadArrayReferences(data, "KillTasks", "Monster", "monster"))
        {
            yield return reference;
        }

        foreach (var path in new[] { "FixedRewards", "SelectRewards" })
        {
            foreach (var reference in ReadArrayReferences(data, path, "Item", "item"))
            {
                yield return reference;
            }
        }
    }

    private static IEnumerable<ContentEditorReferenceValue> ReadArrayReferences(
        JsonElement data,
        string path,
        string property,
        string entityType)
    {
        if (!data.TryGetProperty(path, out var items) || items.ValueKind != JsonValueKind.Array) yield break;
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty(property, out var reference) &&
                reference.ValueKind == JsonValueKind.Object &&
                ContentEditorValidation.ReadLong(reference, "index") is > 0 and var targetIndex)
            {
                yield return new ContentEditorReferenceValue(
                    $"{path}[{index}].{property}",
                    entityType,
                    targetIndex);
            }

            index++;
        }
    }
}
