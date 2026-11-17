using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentNpcEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "npc";

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("Index", "本地索引", "readonly", required: true, readOnly: true, defaultValue: 0),
        ContentEditorValidation.Field("Name", "NPC 名称", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("FileName", "脚本文件", "reference", required: true, referenceType: "script", storeAs: "string"),
        ContentEditorValidation.Field("MapIndex", "所在地图", "reference", required: true, referenceType: "map", storeAs: "index", defaultValue: 0),
        ContentEditorValidation.Field("Location", "坐标", "object", required: true, fields:
        [
            ContentEditorValidation.Field("x", "X", "integer", required: true, defaultValue: 0, min: 0),
            ContentEditorValidation.Field("y", "Y", "integer", required: true, defaultValue: 0, min: 0)
        ]),
        ContentEditorValidation.Field("Rate", "出现概率", "integer", required: true, defaultValue: 100, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("Image", "图像", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("Colour", "颜色", "color", required: true, defaultValue: new { a = 255, r = 255, g = 255, b = 255 }),
        ContentEditorValidation.Field("TimeVisible", "限时出现", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("HourStart", "开始小时", "integer", required: true, defaultValue: 0, min: 0, max: 23),
        ContentEditorValidation.Field("MinuteStart", "开始分钟", "integer", required: true, defaultValue: 0, min: 0, max: 59),
        ContentEditorValidation.Field("HourEnd", "结束小时", "integer", required: true, defaultValue: 0, min: 0, max: 23),
        ContentEditorValidation.Field("MinuteEnd", "结束分钟", "integer", required: true, defaultValue: 1, min: 0, max: 59),
        ContentEditorValidation.Field("MinLev", "最低等级", "integer", required: true, defaultValue: 0, min: 0, max: short.MaxValue),
        ContentEditorValidation.Field("MaxLev", "最高等级", "integer", required: true, defaultValue: 0, min: 0, max: short.MaxValue),
        ContentEditorValidation.Field("DayofWeek", "星期限制", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("ClassRequired", "职业限制", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("Sabuk", "沙巴克", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("FlagNeeded", "需要标记", "integer", required: true, defaultValue: 0),
        ContentEditorValidation.Field("Conquest", "攻城索引", "integer", required: true, defaultValue: 0),
        ContentEditorValidation.Field("ShowOnBigMap", "大地图显示", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("BigMapIcon", "大地图图标", "integer", required: true, defaultValue: 0),
        ContentEditorValidation.Field("CanTeleportTo", "可传送", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("ConquestVisible", "攻城可见", "boolean", required: true, defaultValue: true),
        ContentEditorValidation.Field("CollectQuestIndexes", "可接任务", "reference-array", required: true, referenceType: "quest", storeAs: "index", defaultValue: Array.Empty<int>()),
        ContentEditorValidation.Field("FinishQuestIndexes", "可交任务", "reference-array", required: true, referenceType: "quest", storeAs: "index", defaultValue: Array.Empty<int>())
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "NPC 与脚本入口",
        "编辑 NPC 身份、地图坐标、出现条件和任务入口；脚本内容在 NPC 脚本编辑器中维护。",
        [
            new ContentEditorSectionDto("identity", "身份与入口", "NPC 名称、脚本和地图位置。", Fields.Take(5).ToArray()),
            new ContentEditorSectionDto("display", "显示与时间", "出现概率、图像、颜色和限时窗口。", Fields.Skip(5).Take(9).ToArray()),
            new ContentEditorSectionDto("access", "访问限制", "等级、职业、时间和标记限制。", Fields.Skip(14).Take(8).ToArray()),
            new ContentEditorSectionDto("quests", "任务入口", "NPC 可接取和可交付的任务。", Fields.Skip(22).ToArray())
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
        ValidateIndexAndKey(data, localIndex, contentKey, errors);
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
            yield return "NPC name must not be empty.";
        }

        if (ContentEditorValidation.ReadString(data, "FileName") is { } fileName && string.IsNullOrWhiteSpace(fileName))
        {
            yield return "NPC script file must not be empty.";
        }
    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        return new ContentEditorIdentity(nextLocalIndex, $"{EntityType}.{nextLocalIndex}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return data;
    }

    public static IEnumerable<ContentEditorReferenceValue> GetReferences(JsonElement data)
    {
        if (ContentEditorValidation.ReadLong(data, "MapIndex") is > 0 and var mapIndex)
        {
            yield return new ContentEditorReferenceValue("MapIndex", "map", mapIndex);
        }

        if (ContentEditorValidation.ReadString(data, "FileName") is { Length: > 0 } fileName)
        {
            yield return new ContentEditorReferenceValue("FileName", "script", 0, Key: fileName.ToLowerInvariant());
        }

        foreach (var path in new[] { "CollectQuestIndexes", "FinishQuestIndexes" })
        {
            if (!data.TryGetProperty(path, out var values) || values.ValueKind != JsonValueKind.Array) continue;
            foreach (var value in values.EnumerateArray())
            {
                if (value.TryGetInt64(out var index) && index > 0)
                {
                    yield return new ContentEditorReferenceValue(path, "quest", index);
                }
            }
        }
    }

    private static void ValidateIndexAndKey(
        JsonElement data,
        long localIndex,
        string? contentKey,
        IDictionary<string, string[]> errors)
    {
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
    }
}
