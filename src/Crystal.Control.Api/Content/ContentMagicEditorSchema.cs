using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentMagicEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "magic";

    private static readonly (string Name, int Value)[] SpellValues =
    [
        ("None", 0), ("Fencing", 1), ("Slaying", 2), ("Thrusting", 3), ("HalfMoon", 4),
        ("ShoulderDash", 5), ("TwinDrakeBlade", 6), ("Entrapment", 7), ("FlamingSword", 8),
        ("LionRoar", 9), ("CrossHalfMoon", 10), ("BladeAvalanche", 11), ("ProtectionField", 12),
        ("Rage", 13), ("CounterAttack", 14), ("SlashingBurst", 15), ("Fury", 16), ("ImmortalSkin", 17),
        ("FireBall", 31), ("Repulsion", 32), ("ElectricShock", 33), ("GreatFireBall", 34),
        ("HellFire", 35), ("ThunderBolt", 36), ("Teleport", 37), ("FireBang", 38), ("FireWall", 39),
        ("Lightning", 40), ("FrostCrunch", 41), ("ThunderStorm", 42), ("MagicShield", 43),
        ("TurnUndead", 44), ("Vampirism", 45), ("IceStorm", 46), ("FlameDisruptor", 47),
        ("Mirroring", 48), ("FlameField", 49), ("Blizzard", 50), ("MagicBooster", 51),
        ("MeteorStrike", 52), ("IceThrust", 53), ("FastMove", 54), ("StormEscape", 55),
        ("Healing", 61), ("SpiritSword", 62), ("Poisoning", 63), ("SoulFireBall", 64),
        ("SummonSkeleton", 65), ("Hiding", 67), ("MassHiding", 68), ("SoulShield", 69),
        ("Revelation", 70), ("BlessedArmour", 71), ("EnergyRepulsor", 72), ("TrapHexagon", 73),
        ("Purification", 74), ("MassHealing", 75), ("Hallucination", 76), ("UltimateEnhancer", 77),
        ("SummonShinsu", 78), ("Reincarnation", 79), ("SummonHolyDeva", 80), ("Curse", 81),
        ("Plague", 82), ("PoisonCloud", 83), ("EnergyShield", 84), ("PetEnhancer", 85),
        ("HealingCircle", 86), ("FatalSword", 91), ("DoubleSlash", 92), ("Haste", 93),
        ("FlashDash", 94), ("LightBody", 95), ("HeavenlySword", 96), ("FireBurst", 97), ("Trap", 98),
        ("PoisonSword", 99), ("MoonLight", 100), ("MPEater", 101), ("SwiftFeet", 102),
        ("DarkBody", 103), ("Hemorrhage", 104), ("CrescentSlash", 105), ("MoonMist", 106),
        ("CatTongue", 107), ("Focus", 121), ("StraightShot", 122), ("DoubleShot", 123),
        ("ExplosiveTrap", 124), ("DelayedExplosion", 125), ("Meditation", 126), ("BackStep", 127),
        ("ElementalShot", 128), ("Concentration", 129), ("Stonetrap", 130), ("ElementalBarrier", 131),
        ("SummonVampire", 132), ("VampireShot", 133), ("SummonToad", 134), ("PoisonShot", 135),
        ("CrippleShot", 136), ("SummonSnakes", 137), ("NapalmShot", 138), ("OneWithNature", 139),
        ("BindingShot", 140), ("MentalState", 141), ("150", 150), ("Blink", 151), ("Portal", 152),
        ("BattleCry", 153), ("FireBounce", 154), ("MeteorShower", 155), ("DigOutZombie", 200),
        ("Rubble", 201), ("MapLightning", 202), ("MapLava", 203), ("MapQuake1", 204),
        ("MapQuake2", 205), ("DigOutArmadillo", 206), ("GeneralMeowMeowThunder", 207),
        ("StoneGolemQuake", 208), ("EarthGolemPile", 209), ("TreeQueenRoot", 210),
        ("TreeQueenMassRoots", 211), ("TreeQueenGroundRoots", 212), ("TucsonGeneralRock", 213),
        ("FlyingStatueIceTornado", 214), ("DarkOmaKingNuke", 215), ("HornedSorcererDustTornado", 216),
        ("HornedCommanderRockFall", 217), ("HornedCommanderRockSpike", 218)
    ];

    private static readonly IReadOnlyDictionary<string, int> SpellByName =
        SpellValues.ToDictionary(value => value.Name, value => value.Value, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<int, string> SpellByValue =
        SpellValues.ToDictionary(value => value.Value, value => value.Name);

    private static readonly IReadOnlyList<ContentEditorOptionDto> SpellOptions =
        ContentEditorValidation.Options(SpellValues
            .Select(value => (value.Name, value.Value == 150 ? "旧版 150" : value.Name))
            .ToArray());

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("Name", "技能名称", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("Spell", "技能枚举", "enum", required: true, options: SpellOptions, defaultValue: "None"),
        ContentEditorValidation.Field("BaseCost", "基础消耗", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
        ContentEditorValidation.Field("LevelCost", "等级消耗", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
        ContentEditorValidation.Field("Icon", "图标", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
        ContentEditorValidation.Field("Level1", "一级需求", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
        ContentEditorValidation.Field("Level2", "二级需求", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
        ContentEditorValidation.Field("Level3", "三级需求", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
        ContentEditorValidation.Field("Need1", "一级熟练度", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("Need2", "二级熟练度", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("Need3", "三级熟练度", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("DelayBase", "基础延迟", "integer", required: true, defaultValue: 1800, min: 0, max: uint.MaxValue),
        ContentEditorValidation.Field("DelayReduction", "延迟减免", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue),
        ContentEditorValidation.Field("PowerBase", "道术基础", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("PowerBonus", "道术加成", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("MPowerBase", "魔法基础", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("MPowerBonus", "魔法加成", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
        ContentEditorValidation.Field("MultiplierBase", "倍率基础", "number", required: true, defaultValue: 1.0),
        ContentEditorValidation.Field("MultiplierBonus", "倍率加成", "number", required: true, defaultValue: 0.0),
        ContentEditorValidation.Field("Range", "范围", "integer", required: true, defaultValue: 9, min: 0, max: byte.MaxValue)
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "技能定义",
        "编辑技能等级、消耗、延迟、威力、倍率和范围；Spell 使用控制面独立字符串清单，内容键由枚举值生成。",
        [
            new ContentEditorSectionDto("identity", "身份", "技能名称和枚举。", Fields.Take(2).ToArray()),
            new ContentEditorSectionDto("cost", "消耗与等级", "图标、等级和熟练度需求。", Fields.Skip(2).Take(9).ToArray()),
            new ContentEditorSectionDto("combat", "战斗参数", "延迟、威力和范围。", Fields.Skip(11).ToArray())
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
        if (TryReadSpell(data, out var spellValue, out var spellName) && spellValue != localIndex)
        {
            errors["Spell"] = [$"Spell {spellName} must use localIndex {spellValue}."];
        }

        if (!string.IsNullOrWhiteSpace(contentKey) && TryReadSpell(data, out var value, out _) &&
            !string.Equals(contentKey, $"{EntityType}.{value}", StringComparison.Ordinal))
        {
            errors["contentKey"] = [$"contentKey must be '{EntityType}.{value}'."];
        }

        return errors;
    }

    public static IEnumerable<string> ValidateCompleteness(JsonElement data)
    {
        foreach (var issue in ContentEditorValidation.ValidateRequiredFields(data, Fields))
        {
            yield return issue;
        }

        if (!TryReadSpell(data, out _, out var spellName))
        {
            yield return $"Magic Spell '{ContentEditorValidation.ReadString(data, "Spell")}' is not in the control-plane spell list.";
        }
        else if (string.Equals(spellName, "None", StringComparison.Ordinal))
        {
            yield return "Magic must select a non-None spell.";
        }

        if (ContentEditorValidation.ReadString(data, "Name") is { } name && string.IsNullOrWhiteSpace(name))
        {
            yield return "Magic name must not be empty.";
        }
    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        if (TryReadSpell(data, out var value, out _))
        {
            return new ContentEditorIdentity(value, $"{EntityType}.{value}");
        }

        return new ContentEditorIdentity(nextLocalIndex, $"{EntityType}.{nextLocalIndex}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return ContentEditorJson.Normalize(data, node =>
        {
            if (TryReadSpell(data, out var value, out var name))
            {
                node["Spell"] = name;
            }
        });
    }

    public static bool TryReadSpell(JsonElement data, out long value, out string name)
    {
        value = 0;
        name = string.Empty;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("Spell", out var spell))
        {
            return false;
        }

        if (spell.ValueKind == JsonValueKind.String)
        {
            var text = spell.GetString() ?? string.Empty;
            if (SpellByName.TryGetValue(text, out var numeric))
            {
                value = numeric;
                name = SpellByValue[numeric];
                return true;
            }

            if (int.TryParse(text, out numeric) && SpellByValue.TryGetValue(numeric, out var numericName))
            {
                value = numeric;
                name = numericName;
                return true;
            }
        }

        if (spell.ValueKind == JsonValueKind.Number && spell.TryGetInt32(out var direct) &&
            SpellByValue.TryGetValue(direct, out var directName))
        {
            value = direct;
            name = directName;
            return true;
        }

        return false;
    }
}
