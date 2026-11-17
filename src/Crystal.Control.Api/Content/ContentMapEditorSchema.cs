using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentMapEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "map";

    private static readonly IReadOnlyList<ContentEditorOptionDto> LightOptions = Options(
        ("Normal", "正常"),
        ("Dawn", "黎明"),
        ("Day", "白天"),
        ("Evening", "黄昏"),
        ("Night", "夜晚"));

    public static readonly ContentEditorSchemaDto Schema = new(
        EntityType,
        SchemaVersion,
        "地图元数据",
        "编辑地图身份、客户端表现、访问规则、战斗环境和人口限制。传送、安全区、刷新点、NPC 与矿区保持只读。",
        [
            new ContentEditorSectionDto("identity", "基础信息", "地图旧索引、资源文件名和显示标题。", [
                Field("Index", "旧索引", "integer", required: true, readOnly: true, defaultValue: null, min: 0, max: int.MaxValue),
                Field("FileName", "资源文件名", "string", required: true, defaultValue: ""),
                Field("Title", "地图标题", "string", required: true, defaultValue: "")
            ]),
            new ContentEditorSectionDto("presentation", "客户端表现", "小地图、大地图、音乐、光照和天气。", [
                Field("MiniMap", "小地图", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
                Field("BigMap", "大地图", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
                Field("Music", "音乐", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
                Field("Light", "光照", "enum", required: true, defaultValue: "Normal", options: LightOptions),
                Field("MapDarkLight", "地图暗光", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("WeatherParticles", "天气粒子（只读）", "string", required: true, readOnly: true, defaultValue: "None")
            ]),
            new ContentEditorSectionDto("access", "访问控制", "传送、重连、随机、逃离、回城和复活规则。", [
                Field("NoTeleport", "禁止传送", "boolean", required: true, defaultValue: false),
                Field("NoReconnect", "禁止重连", "boolean", required: true, defaultValue: false),
                Field("NoReconnectMap", "重连目标地图", "string", required: true, defaultValue: ""),
                Field("NoRandom", "禁止随机传送", "boolean", required: true, defaultValue: false),
                Field("NoEscape", "禁止逃离", "boolean", required: true, defaultValue: false),
                Field("NoRecall", "禁止召回", "boolean", required: true, defaultValue: false),
                Field("NoTownTeleport", "禁止回城传送", "boolean", required: true, defaultValue: false),
                Field("NoReincarnation", "禁止转生", "boolean", required: true, defaultValue: false),
                Field("NeedHole", "需要洞口", "boolean", required: true, defaultValue: false),
                Field("NoPosition", "禁止定位", "boolean", required: true, defaultValue: false)
            ]),
            new ContentEditorSectionDto("combat", "战斗与环境", "战斗状态、火焰/雷电伤害、防火墙和物品限制。", [
                Field("Fight", "战斗地图", "boolean", required: true, defaultValue: false),
                Field("NoFight", "禁止战斗", "boolean", required: true, defaultValue: false),
                Field("Fire", "火焰环境", "boolean", required: true, defaultValue: false),
                Field("FireDamage", "火焰伤害", "integer", required: true, defaultValue: 0, min: 0, max: int.MaxValue),
                Field("Lightning", "雷电环境", "boolean", required: true, defaultValue: false),
                Field("LightningDamage", "雷电伤害", "integer", required: true, defaultValue: 0, min: 0, max: int.MaxValue),
                Field("FireWallLimit", "限制防火墙", "boolean", required: true, defaultValue: false),
                Field("FireWallCount", "防火墙数量", "integer", required: true, defaultValue: 0, min: 0, max: int.MaxValue),
                Field("NoDrug", "禁止药品", "boolean", required: true, defaultValue: false),
                Field("NoThrowItem", "禁止丢弃物品", "boolean", required: true, defaultValue: false),
                Field("NoDropPlayer", "玩家死亡不掉落", "boolean", required: true, defaultValue: false),
                Field("NoDropMonster", "怪物死亡不掉落", "boolean", required: true, defaultValue: false)
            ]),
            new ContentEditorSectionDto("population", "人口与限制", "名称、坐骑、组队、宠物、英雄和智能生物规则。", [
                Field("NoNames", "隐藏名称", "boolean", required: true, defaultValue: false),
                Field("NoMount", "禁止坐骑", "boolean", required: true, defaultValue: false),
                Field("NeedBridle", "需要马鞍", "boolean", required: true, defaultValue: false),
                Field("NoGroup", "禁止组队", "boolean", required: true, defaultValue: false),
                Field("RequiredGroup", "要求组队", "boolean", required: true, defaultValue: false),
                Field("RequiredGroupSize", "要求组队人数", "integer", required: true, defaultValue: 0, min: 0, max: int.MaxValue),
                Field("NoPets", "禁止宠物", "boolean", required: true, defaultValue: false),
                Field("NoIntelligentCreatures", "禁止智能生物", "boolean", required: true, defaultValue: false),
                Field("NoHero", "禁止英雄", "boolean", required: true, defaultValue: false),
                Field("NoExperience", "禁止获得经验", "boolean", required: true, defaultValue: false)
            ]),
            new ContentEditorSectionDto("territory", "矿区与攻城", "矿区索引和攻城地图标记。", [
                Field("GT", "攻城地图", "boolean", required: true, defaultValue: false),
                Field("GTIndex", "攻城索引", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("MineIndex", "矿区索引", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue)
            ])
        ]);

    public static Dictionary<string, string[]> ValidateStructure(JsonElement data, long localIndex, string? contentKey)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            errors["data"] = ["data must be a JSON object."];
            return errors;
        }

        foreach (var field in TopLevelFields())
        {
            if (!data.TryGetProperty(field.Path, out var fieldValue)) continue;
            ValidateField(field, fieldValue, errors);
        }

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
        if (data.ValueKind != JsonValueKind.Object)
        {
            yield return "Map data must be a JSON object.";
            yield break;
        }

        foreach (var field in TopLevelFields())
        {
            if (!data.TryGetProperty(field.Path, out _))
            {
                yield return $"Missing required map field '{field.Path}'.";
            }
        }

        if (data.TryGetProperty("FileName", out var fileName) &&
            fileName.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(fileName.GetString()))
        {
            yield return "Map file name must not be empty.";
        }

        if (data.TryGetProperty("Title", out var title) &&
            title.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(title.GetString()))
        {
            yield return "Map title must not be empty.";
        }
    }

    private static void ValidateField(
        ContentEditorFieldDto field,
        JsonElement value,
        Dictionary<string, string[]> errors)
    {
        switch (field.Kind)
        {
            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                {
                    errors[field.Path] = [$"{field.Path} must be an integer."];
                    return;
                }

                if ((field.Min is not null && number < field.Min.Value) ||
                    (field.Max is not null && number > field.Max.Value))
                {
                    errors[field.Path] =
                    [
                        $"{field.Path} must be between {field.Min?.ToString() ?? "-∞"} and {field.Max?.ToString() ?? "∞"}."
                    ];
                }

                return;
            case "string":
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors[field.Path] = [$"{field.Path} must be a string."];
                }

                return;
            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors[field.Path] = [$"{field.Path} must be a boolean."];
                }

                return;
            case "enum":
                ValidateEnum(field, value, errors);
                return;
        }
    }

    private static void ValidateEnum(
        ContentEditorFieldDto field,
        JsonElement value,
        Dictionary<string, string[]> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors[field.Path] = [$"{field.Path} must be a string."];
            return;
        }

        var optionValues = (field.Options ?? []).Select(option => option.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (!optionValues.Contains(value.GetString() ?? string.Empty))
        {
            errors[field.Path] = [$"{field.Path} must be one of the known values."];
        }
    }

    private static IEnumerable<ContentEditorFieldDto> TopLevelFields()
    {
        return Schema.Sections.SelectMany(section => section.Fields);
    }

    private static ContentEditorFieldDto Field(
        string path,
        string label,
        string kind,
        bool required = false,
        bool readOnly = false,
        object? defaultValue = null,
        long? min = null,
        long? max = null,
        IReadOnlyList<ContentEditorOptionDto>? options = null)
    {
        return new ContentEditorFieldDto(path, label, kind, required, readOnly, defaultValue, min, max, options);
    }

    private static IReadOnlyList<ContentEditorOptionDto> Options(params (string Value, string Label)[] values)
    {
        return values.Select(value => new ContentEditorOptionDto(value.Value, value.Label)).ToArray();
    }
}
