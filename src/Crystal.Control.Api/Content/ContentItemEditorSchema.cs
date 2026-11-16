using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentItemEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "item";

    private static readonly IReadOnlyList<ContentEditorOptionDto> ItemTypes = Options(
        ("Nothing", "空"), ("Weapon", "武器"), ("Armour", "衣服"), ("Helmet", "头盔"),
        ("Necklace", "项链"), ("Bracelet", "手镯"), ("Ring", "戒指"), ("Amulet", "护符"),
        ("Belt", "腰带"), ("Boots", "鞋子"), ("Stone", "宝石"), ("Torch", "火把"),
        ("Potion", "药品"), ("Ore", "矿石"), ("Meat", "肉类"), ("CraftingMaterial", "材料"),
        ("Scroll", "卷轴"), ("Gem", "宝石"), ("Mount", "坐骑"), ("Book", "书籍"),
        ("Script", "脚本道具"), ("Reins", "缰绳"), ("Bells", "铃铛"), ("Saddle", "马鞍"),
        ("Ribbon", "装饰"), ("Mask", "面具"), ("Food", "食物"), ("Hook", "鱼钩"),
        ("Float", "浮漂"), ("Bait", "鱼饵"), ("Finder", "探测器"), ("Reel", "卷线器"),
        ("Fish", "鱼"), ("Quest", "任务道具"), ("Awakening", "觉醒"), ("Pets", "宠物"),
        ("Transform", "变身"), ("Deco", "装饰品"), ("Socket", "镶嵌"), ("MonsterSpawn", "怪物召唤"),
        ("SiegeAmmo", "攻城弹药"), ("SealedHero", "封印英雄"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> Grades = Options(
        ("None", "无"), ("Common", "普通"), ("Rare", "稀有"), ("Legendary", "传说"),
        ("Mythical", "神话"), ("Heroic", "英雄"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> RequiredTypes = Options(
        ("Level", "等级"), ("MaxAC", "最大防御"), ("MaxMAC", "最大魔御"), ("MaxDC", "最大攻击"),
        ("MaxMC", "最大魔法"), ("MaxSC", "最大道术"), ("MaxLevel", "最大等级"),
        ("MinAC", "最小防御"), ("MinMAC", "最小魔御"), ("MinDC", "最小攻击"),
        ("MinMC", "最小魔法"), ("MinSC", "最小道术"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> RequiredClasses = Options(
        ("None", "通用"), ("Warrior", "战士"), ("Wizard", "法师"), ("Taoist", "道士"),
        ("Assassin", "刺客"), ("Archer", "弓箭手"), ("WarWizTao", "战法道"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> RequiredGenders = Options(
        ("None", "不限"), ("Male", "男"), ("Female", "女"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> ItemSets = Options(
        ("None", "无"), ("Spirit", "精神"), ("Recall", "召回"), ("RedOrchid", "红兰"),
        ("RedFlower", "红花"), ("Smash", "粉碎"), ("HwanDevil", "幻魔"), ("Purity", "纯洁"),
        ("FiveString", "五弦"), ("Mundane", "凡尘"), ("NokChi", "绿气"), ("TaoProtect", "道护"),
        ("Mir", "MIR"), ("Bone", "骨"), ("Bug", "虫"), ("WhiteGold", "白金"),
        ("WhiteGoldH", "白金H"), ("RedJade", "红玉"), ("RedJadeH", "红玉H"),
        ("Nephrite", "软玉"), ("NephriteH", "软玉H"), ("Whisker1", "须1"), ("Whisker2", "须2"),
        ("Whisker3", "须3"), ("Whisker4", "须4"), ("Whisker5", "须5"),
        ("Hyeolryong", "血龙"), ("Monitor", "监视"), ("Oppressive", "压迫"), ("Paeok", "佩玉"),
        ("Sulgwan", "雪冠"), ("BlueFrost", "蓝霜"), ("DarkGhost", "暗鬼"),
        ("BlueFrostH", "蓝霜H"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> BindModes = Options(
        ("None", "无"), ("DontDeathdrop", "死亡不掉落"), ("DontDrop", "不可丢弃"),
        ("DontSell", "不可出售"), ("DontStore", "不可存仓"), ("DontTrade", "不可交易"),
        ("DontRepair", "不可修理"), ("DontUpgrade", "不可升级"), ("DestroyOnDrop", "丢弃销毁"),
        ("BreakOnDeath", "死亡破碎"), ("BindOnEquip", "装备绑定"), ("NoSRepair", "不可特殊修理"),
        ("NoWeddingRing", "不可婚戒"), ("UnableToRent", "不可出租"),
        ("UnableToDisassemble", "不可分解"), ("NoMail", "不可邮寄"), ("NoHero", "英雄不可用"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> UniqueModes = Options(
        ("None", "无"), ("Paralize", "麻痹"), ("Teleport", "传送"), ("ClearRing", "清环"),
        ("Protection", "保护"), ("Revival", "复活"), ("Muscle", "力量"), ("Flame", "火焰"),
        ("Healing", "治疗"), ("Probe", "探测"), ("Skill", "技能"), ("NoDuraLoss", "无耐久损失"),
        ("Blink", "闪现"));

    private static readonly IReadOnlyList<ContentEditorOptionDto> StatOptions = Options(
        ("MinAC", "最小防御"), ("MaxAC", "最大防御"), ("MinMAC", "最小魔御"), ("MaxMAC", "最大魔御"),
        ("MinDC", "最小攻击"), ("MaxDC", "最大攻击"), ("MinMC", "最小魔法"), ("MaxMC", "最大魔法"),
        ("MinSC", "最小道术"), ("MaxSC", "最大道术"), ("Accuracy", "准确"), ("Agility", "敏捷"),
        ("HP", "生命"), ("MP", "魔法值"), ("AttackSpeed", "攻速"), ("Luck", "幸运"),
        ("BagWeight", "背包重量"), ("HandWeight", "腕力"), ("WearWeight", "穿戴重量"),
        ("Reflect", "反伤"), ("Strong", "强度"), ("Holy", "神圣"), ("Freezing", "冰冻"),
        ("PoisonAttack", "施毒"), ("MagicResist", "魔御"), ("PoisonResist", "毒抗"),
        ("HealthRecovery", "生命恢复"), ("SpellRecovery", "魔法恢复"), ("PoisonRecovery", "毒恢复"),
        ("CriticalRate", "暴击率"), ("CriticalDamage", "暴击伤害"),
        ("MaxACRatePercent", "最大防御率"), ("MaxMACRatePercent", "最大魔御率"),
        ("MaxDCRatePercent", "最大攻击率"), ("MaxMCRatePercent", "最大魔法率"),
        ("MaxSCRatePercent", "最大道术率"), ("AttackSpeedRatePercent", "攻速率"),
        ("HPRatePercent", "生命率"), ("MPRatePercent", "魔法率"),
        ("HPDrainRatePercent", "生命吸取率"), ("ExpRatePercent", "经验率"),
        ("ItemDropRatePercent", "物品掉落率"), ("GoldDropRatePercent", "金币掉落率"),
        ("MineRatePercent", "采矿率"), ("GemRatePercent", "宝石率"),
        ("FishRatePercent", "钓鱼率"), ("CraftRatePercent", "制作率"),
        ("SkillGainMultiplier", "技能成长倍率"), ("AttackBonus", "攻击加成"),
        ("LoverExpRatePercent", "伴侣经验率"), ("MentorDamageRatePercent", "师徒伤害率"),
        ("MentorExpRatePercent", "师徒经验率"), ("DamageReductionPercent", "伤害减免率"),
        ("EnergyShieldPercent", "能量盾率"), ("EnergyShieldHPGain", "能量盾生命"),
        ("ManaPenaltyPercent", "魔法惩罚率"), ("TeleportManaPenaltyPercent", "传送魔法惩罚率"),
        ("Hero", "英雄"));

    private static readonly HashSet<string> BindModeNames = BindModes.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> UniqueModeNames = UniqueModes.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> StatNames = StatOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);

    private static readonly ContentEditorFieldDto[] RandomStatFields = BuildRandomStatFields();

    public static readonly ContentEditorSchemaDto Schema = new(
        EntityType,
        SchemaVersion,
        "物品与装备",
        "编辑 ItemInfo 全部字段、属性值和随机属性。保存草稿不会直接影响运行中的区服。",
        [
            new ContentEditorSectionDto("identity", "基础信息", "物品身份、类型、等级和客户端表现。", [
                Field("Index", "旧索引", "integer", required: true, readOnly: true, defaultValue: null, min: 0, max: int.MaxValue),
                Field("Name", "名称", "string", required: true, defaultValue: ""),
                Field("Type", "物品类型", "enum", required: true, defaultValue: "Nothing", options: ItemTypes),
                Field("Grade", "品质", "enum", required: true, defaultValue: "None", options: Grades),
                Field("Set", "套装", "enum", required: true, defaultValue: "None", options: ItemSets),
                Field("Shape", "外观形状", "integer", required: true, defaultValue: 0, min: short.MinValue, max: short.MaxValue),
                Field("Image", "图像索引", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
                Field("Effect", "特效", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("Light", "亮度", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue)
            ]),
            new ContentEditorSectionDto("requirements", "使用要求", "等级、职业、性别和需求值。", [
                Field("RequiredType", "需求类型", "enum", required: true, defaultValue: "Level", options: RequiredTypes),
                Field("RequiredClass", "需求职业", "enum", required: true, defaultValue: "None", options: RequiredClasses),
                Field("RequiredGender", "需求性别", "enum", required: true, defaultValue: "None", options: RequiredGenders),
                Field("RequiredAmount", "需求数值", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue)
            ]),
            new ContentEditorSectionDto("inventory", "经济与背包", "重量、持久、价格、堆叠和绑定规则。", [
                Field("Weight", "重量", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("Durability", "持久", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
                Field("StackSize", "堆叠上限", "integer", required: true, defaultValue: 1, min: 0, max: ushort.MaxValue),
                Field("Price", "默认售价", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue),
                Field("Slots", "孔位", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("StartItem", "初始物品", "boolean", required: true, defaultValue: false),
                Field("Bind", "绑定规则", "flags", required: true, defaultValue: "None", options: BindModes),
                Field("Unique", "特殊能力", "flags", required: true, defaultValue: "None", options: UniqueModes),
                Field("RandomStatsId", "随机属性模板", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("ToolTip", "提示文本", "string", required: true, defaultValue: "")
            ]),
            new ContentEditorSectionDto("flags", "行为开关", "运行时读取的物品行为标记。", [
                Field("NeedIdentify", "需要鉴定", "boolean", required: true, defaultValue: false),
                Field("ShowGroupPickup", "组队拾取提示", "boolean", required: true, defaultValue: false),
                Field("GlobalDropNotify", "全服掉落通知", "boolean", required: true, defaultValue: false),
                Field("ClassBased", "按职业区分", "boolean", required: true, defaultValue: false),
                Field("LevelBased", "按等级区分", "boolean", required: true, defaultValue: false),
                Field("CanMine", "可采矿", "boolean", required: true, defaultValue: false),
                Field("CanFastRun", "可快速穿戴", "boolean", required: true, defaultValue: false),
                Field("CanAwakening", "可觉醒", "boolean", required: true, defaultValue: false)
            ]),
            new ContentEditorSectionDto("stats", "固定属性", "Stats.Values 中的固定属性键值。", [
                Field("Stats", "属性值", "stat-map", required: true, defaultValue: new { Values = new SortedDictionary<string, int>(StringComparer.Ordinal) }, options: StatOptions)
            ]),
            new ContentEditorSectionDto("random-stats", "随机属性", "RandomItemStat 的 70 个字节字段。", RandomStatFields)
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
            if (field.Kind == "stat-map") continue;
            ValidateField(field, fieldValue, errors);
        }

        ValidateObject(data, "RandomStats", RandomStatFields, errors);
        ValidateStats(data, errors);

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
            yield return "Item data must be a JSON object.";
            yield break;
        }

        foreach (var field in TopLevelFields())
        {
            if (!data.TryGetProperty(field.Path, out _))
            {
                yield return $"Missing required item field '{field.Path}'.";
            }
        }

        if (data.TryGetProperty("Name", out var name) &&
            name.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(name.GetString()))
        {
            yield return "Item name must not be empty.";
        }

        if (data.TryGetProperty("Stats", out var stats))
        {
            if (stats.ValueKind != JsonValueKind.Object)
            {
                yield return "Item field 'Stats' must be an object.";
            }
            else if (!stats.TryGetProperty("Values", out _))
            {
                yield return "Missing required item field 'Stats.Values'.";
            }
        }

        if (data.TryGetProperty("RandomStats", out var randomStats))
        {
            if (randomStats.ValueKind != JsonValueKind.Object)
            {
                yield return "Item field 'RandomStats' must be an object.";
            }
            else
            {
                foreach (var field in RandomStatFields)
                {
                    var namePart = field.Path["RandomStats.".Length..];
                    if (!randomStats.TryGetProperty(namePart, out _))
                    {
                        yield return $"Missing random stat field 'RandomStats.{namePart}'.";
                    }
                }
            }
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
                ValidateOptions(field, value, errors, allowMultiple: false);
                return;
            case "flags":
                ValidateOptions(field, value, errors, allowMultiple: true);
                return;
            case "object":
                if (value.ValueKind != JsonValueKind.Object)
                {
                    errors[field.Path] = [$"{field.Path} must be an object."];
                }

                return;
        }
    }

    private static void ValidateOptions(
        ContentEditorFieldDto field,
        JsonElement value,
        Dictionary<string, string[]> errors,
        bool allowMultiple)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors[field.Path] = [$"{field.Path} must be a string."];
            return;
        }

        var optionValues = (field.Options ?? []).Select(option => option.Value)
            .ToHashSet(StringComparer.Ordinal);
        var text = value.GetString() ?? string.Empty;
        string[] values = allowMultiple
            ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [text];
        if (values.Length == 0 || values.Any(option => !optionValues.Contains(option)))
        {
            errors[field.Path] =
            [
                allowMultiple
                    ? $"{field.Path} must be a comma-separated list of known flags."
                    : $"{field.Path} must be one of the known values."
            ];
        }
    }

    public static bool TryGetStatOption(string value, out ContentEditorOptionDto? option)
    {
        option = StatOptions.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.Ordinal));
        return option is not null;
    }

    private static void ValidateObject(
        JsonElement data,
        string objectName,
        IReadOnlyList<ContentEditorFieldDto> fields,
        Dictionary<string, string[]> errors)
    {
        if (!data.TryGetProperty(objectName, out var value)) return;
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors[objectName] = [$"{objectName} must be an object."];
            return;
        }

        foreach (var field in fields)
        {
            var name = field.Path[(objectName.Length + 1)..];
            if (!value.TryGetProperty(name, out var fieldValue)) continue;
            if (!fieldValue.TryGetInt64(out var number) || number is < 0 or > byte.MaxValue)
            {
                errors[field.Path] = [$"{field.Path} must be an integer between 0 and 255."];
            }
        }
    }

    private static void ValidateStats(JsonElement data, Dictionary<string, string[]> errors)
    {
        if (!data.TryGetProperty("Stats", out var stats)) return;
        if (stats.ValueKind != JsonValueKind.Object)
        {
            errors["Stats"] = ["Stats must be an object."];
            return;
        }

        if (!stats.TryGetProperty("Values", out var values)) return;
        if (values.ValueKind != JsonValueKind.Object)
        {
            errors["Stats.Values"] = ["Stats.Values must be an object."];
            return;
        }

        foreach (var property in values.EnumerateObject())
        {
            var path = $"Stats.Values.{property.Name}";
            if (!StatNames.Contains(property.Name))
            {
                errors[path] = [$"Unknown stat '{property.Name}'."];
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out _))
            {
                errors[path] = [$"{path} must be a 32-bit integer."];
            }
        }
    }

    private static IEnumerable<ContentEditorFieldDto> TopLevelFields()
    {
        return Schema.Sections
            .SelectMany(section => section.Fields)
            .Where(field => !field.Path.StartsWith("RandomStats.", StringComparison.Ordinal));
    }

    private static ContentEditorFieldDto[] BuildRandomStatFields()
    {
        var prefixes = new (string Prefix, string Label)[]
        {
            ("MaxDura", "最大持久"), ("MaxAc", "最大防御"), ("MaxMac", "最大魔御"),
            ("MaxDc", "最大攻击"), ("MaxMc", "最大魔法"), ("MaxSc", "最大道术"),
            ("Accuracy", "准确"), ("Agility", "敏捷"), ("Hp", "生命"), ("Mp", "魔法值"),
            ("Strong", "强度"), ("MagicResist", "魔御"), ("PoisonResist", "毒抗"),
            ("HpRecov", "生命恢复"), ("MpRecov", "魔法恢复"), ("PoisonRecov", "毒恢复"),
            ("CriticalRate", "暴击率"), ("CriticalDamage", "暴击伤害"), ("Freeze", "冰冻"),
            ("PoisonAttack", "施毒"), ("AttackSpeed", "攻速"), ("Luck", "幸运"),
            ("Curse", "诅咒"), ("Slot", "孔位")
        };
        var suffixes = new (string Suffix, string Label)[] { ("Chance", "概率"), ("StatChance", "属性概率"), ("MaxStat", "最大值") };
        var fields = new List<ContentEditorFieldDto>();
        foreach (var (prefix, prefixLabel) in prefixes)
        {
            foreach (var (suffix, suffixLabel) in suffixes)
            {
                if (prefix == "Curse" && suffix != "Chance") continue;
                var name = $"{prefix}{suffix}";
                fields.Add(new ContentEditorFieldDto(
                    $"RandomStats.{name}",
                    $"{prefixLabel}{suffixLabel}",
                    "integer",
                    Required: true,
                    DefaultValue: 0,
                    Min: 0,
                    Max: byte.MaxValue));
            }
        }

        return fields.ToArray();
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
        IReadOnlyList<ContentEditorOptionDto>? options = null,
        IReadOnlyList<ContentEditorFieldDto>? fields = null)
    {
        return new ContentEditorFieldDto(path, label, kind, required, readOnly, defaultValue, min, max, options, fields);
    }

    private static IReadOnlyList<ContentEditorOptionDto> Options(params (string Value, string Label)[] values)
    {
        return values.Select(value => new ContentEditorOptionDto(value.Value, value.Label)).ToArray();
    }
}
