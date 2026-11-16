using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentMonsterEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "monster";

    private static readonly IReadOnlyList<ContentEditorOptionDto> MonsterImages = EnumOptions(
        "Guard", "TaoistGuard", "Guard2", "Hen", "Deer", "Scarecrow", "HookingCat",
        "RakingCat", "Yob", "Oma", "CannibalPlant", "ForestYeti", "SpittingSpider", "ChestnutTree", "EbonyTree",
        "LargeMushroom", "CherryTree", "OmaFighter", "OmaWarrior", "CaveBat", "CaveMaggot", "Scorpion", "Skeleton",
        "BoneFighter", "AxeSkeleton", "BoneWarrior", "BoneElite", "Dung", "Dark", "WoomaSoldier", "WoomaFighter",
        "WoomaWarrior", "FlamingWooma", "WoomaGuardian", "WoomaTaurus", "WhimperingBee", "GiantWorm", "Centipede", "BlackMaggot",
        "Tongs", "EvilTongs", "EvilCentipede", "BugBat", "BugBatMaggot", "WedgeMoth", "RedBoar", "BlackBoar",
        "SnakeScorpion", "WhiteBoar", "EvilSnake", "BombSpider", "RootSpider", "SpiderBat", "VenomSpider", "GangSpider",
        "GreatSpider", "LureSpider", "BigApe", "EvilApe", "GrayEvilApe", "RedEvilApe", "CrystalSpider", "RedMoonEvil",
        "BigRat", "ZumaArcher", "ZumaStatue", "ZumaGuardian", "RedThunderZuma", "ZumaTaurus", "DigOutZombie", "ClZombie",
        "NdZombie", "CrawlerZombie", "ShamanZombie", "Ghoul", "KingScorpion", "KingHog", "DarkDevil", "BoneFamiliar",
        "Shinsu", "Shinsu1", "SpiderFrog", "HoroBlaster", "BlueHoroBlaster", "KekTal", "VioletKekTal", "Khazard",
        "RoninGhoul", "ToxicGhoul", "BoneCaptain", "BoneSpearman", "BoneBlademan", "BoneArcher", "BoneLord", "Minotaur",
        "IceMinotaur", "ElectricMinotaur", "WindMinotaur", "FireMinotaur", "RightGuard", "LeftGuard", "MinotaurKing", "FrostTiger",
        "Sheep", "Wolf", "ShellNipper", "Keratoid", "GiantKeratoid", "SkyStinger", "SandWorm", "VisceralWorm",
        "RedSnake", "TigerSnake", "Yimoogi", "GiantWhiteSnake", "BlueSnake", "YellowSnake", "HolyDeva", "AxeOma",
        "SwordOma", "CrossbowOma", "WingedOma", "FlailOma", "OmaGuard", "YinDevilNode", "YangDevilNode", "OmaKing",
        "BlackFoxman", "RedFoxman", "WhiteFoxman", "TrapRock", "GuardianRock", "ThunderElement", "CloudElement", "GreatFoxSpirit",
        "HedgeKekTal", "BigHedgeKekTal", "RedFrogSpider", "BrownFrogSpider", "ArcherGuard", "KatanaGuard", "ArcherGuard2", "Pig",
        "Bull", "Bush", "ChristmasTree", "HighAssassin", "DarkDustPile", "DarkBrownWolf", "Football", "GingerBreadman",
        "HalloweenScythe", "GhastlyLeecher", "CyanoGhast", "MutatedManworm", "CrazyManworm", "MudPile", "TailedLion", "Behemoth",
        "DarkDevourer", "PoisonHugger", "Hugger", "MutatedHugger", "DreamDevourer", "Treasurebox", "SnowPile", "Snowman",
        "SnowTree", "GiantEgg", "RedTurtle", "GreenTurtle", "BlueTurtle", "Catapult1", "Catapult2", "OldSpittingSpider",
        "SiegeRepairman", "BlueSanta", "BattleStandard", "WingedBullLord", "RedYimoogi", "LionRiderMale", "LionRiderFemale", "Tornado",
        "FlameTiger", "WingedTigerLord", "TowerTurtle", "FinialTurtle", "TurtleKing", "DarkTurtle", "LightTurtle", "DarkSwordOma",
        "DarkAxeOma", "DarkCrossbowOma", "DarkWingedOma", "BoneWhoo", "DarkSpider", "ViscusWorm", "ViscusCrawler", "CrawlerLave",
        "DarkYob", "FlamingMutant", "StoningStatue", "FlyingStatue", "ValeBat", "Weaver", "VenomWeaver", "CrackingWeaver",
        "ArmingWeaver", "CrystalWeaver", "FrozenZumaStatue", "FrozenZumaGuardian", "FrozenRedZuma", "GreaterWeaver", "SpiderWarrior", "SpiderBarbarian",
        "HellSlasher", "HellPirate", "HellCannibal", "HellKeeper", "HellBolt", "WitchDoctor", "ManectricHammer", "ManectricClub",
        "ManectricClaw", "ManectricStaff", "NamelessGhost", "DarkGhost", "ChaosGhost", "ManectricBlest", "ManectricKing", "Blank2",
        "IcePillar", "FrostYeti", "ManectricSlave", "TrollHammer", "TrollBomber", "TrollStoner", "TrollKing", "FlameSpear",
        "FlameMage", "FlameScythe", "FlameAssassin", "FlameQueen", "HellKnight1", "HellKnight2", "HellKnight3", "HellKnight4",
        "HellLord", "WaterGuard", "IceGuard", "ElementGuard", "DemonGuard", "KingGuard", "Snake10", "Snake11",
        "Snake12", "Snake13", "Snake14", "Snake15", "Snake16", "Snake17", "DeathCrawler", "BurningZombie",
        "MudZombie", "FrozenZombie", "UndeadWolf", "DemonWolf", "WhiteMammoth", "DarkBeast", "LightBeast", "BloodBaboon",
        "HardenRhino", "AncientBringer", "FightingCat", "FireCat", "CatWidow", "StainHammerCat", "BlackHammerCat", "StrayCat",
        "CatShaman", "Jar1", "Jar2", "SeedingsGeneral", "RestlessJar", "GeneralMeowMeow", "Bunny", "Tucson",
        "TucsonFighter", "TucsonMage", "TucsonWarrior", "Armadillo", "ArmadilloElder", "TucsonEgg", "PlaguedTucson", "SandSnail",
        "CannibalTentacles", "TucsonGeneral", "GasToad", "Mantis", "SwampWarrior", "AssassinBird", "RhinoWarrior", "RhinoPriest",
        "ElephantMan", "StoneGolem", "EarthGolem", "TreeGuardian", "TreeQueen", "PeacockSpider", "DarkBaboon", "TwinHeadBeast",
        "OmaCannibal", "OmaBlest", "OmaSlasher", "OmaAssassin", "OmaMage", "OmaWitchDoctor", "LightningBead", "HealingBead",
        "PowerUpBead", "DarkOmaKing", "CaveStatue", "Mandrill", "PlagueCrab", "CreeperPlant", "FloatingWraith", "ArmedPlant",
        "AvengerPlant", "Nadz", "AvengingSpirit", "AvengingWarrior", "AxePlant", "WoodBox", "ClawBeast", "DarkCaptain",
        "SackWarrior", "WereTiger", "KingHydrax", "Hydrax", "HornedMage", "BlueSoul", "HornedArcher", "ColdArcher",
        "HornedWarrior", "FloatingRock", "ScalyBeast", "HornedSorceror", "BoulderSpirit", "HornedCommander", "MoonStone", "SunStone",
        "LightningStone", "Turtlegrass", "ManTree", "Bear", "Leopard", "ChieftainArcher", "ChieftainSword", "StoningSpider",
        "VampireSpider", "SpittingToad", "SnakeTotem", "CharmedSnake", "FrozenSoldier", "FrozenFighter", "FrozenArcher", "FrozenKnight",
        "FrozenGolem", "IcePhantom", "SnowWolf", "SnowWolfKing", "WaterDragon", "BlackTortoise", "Manticore", "DragonWarrior",
        "DragonArcher", "Kirin", "Guard3", "ArcherGuard3", "Bunny2", "FrozenMiner", "FrozenAxeman", "FrozenMagician",
        "SnowYeti", "IceCrystalSoldier", "DarkWraith", "DarkSpirit", "CrystalBeast", "RedOrb", "BlueOrb", "YellowOrb",
        "GreenOrb", "WhiteOrb", "FatalLotus", "AntCommander", "CargoBoxwithlogo", "Doe", "Reindeer", "AngryReindeer",
        "CargoBox", "Ram1", "Ram2", "Kite", "PurpleFaeFlower", "Furball", "GlacierSnail", "FurbolgWarrior",
        "FurbolgArcher", "FurbolgCommander", "RedFaeFlower", "FurbolgGuard", "GlacierBeast", "GlacierWarrior", "ShardGuardian", "WarriorScroll",
        "TaoistScroll", "WizardScroll", "AssassinScroll", "HoodedSummoner", "HoodedIceMage", "HoodedPriest", "ShardMaiden", "KingKong",
        "WarBear", "ReaperPriest", "ReaperWizard", "ReaperAssassin", "LivingVines", "BlueMonk", "MutantBeserker", "MutantGuardian",
        "MutantHighPriest", "MysteriousMage", "FeatheredWolf", "MysteriousAssassin", "MysteriousMonk", "ManEatingPlant", "HammerDwarf", "ArcherDwarf",
        "NobleWarrior", "NobleArcher", "NoblePriest", "NobleAssassin", "Swain", "RedMutantPlant", "BlueMutantPlant", "UndeadHammerDwarf",
        "UndeadDwarfArcher", "AncientStoneGolem", "Serpentirian", "Butcher", "Riklebites", "FeralTundraFurbolg", "FeralFlameFurbolg", "ArcaneTotem",
        "SpectralWraith", "BabyMagmaDragon", "BloodLord", "SerpentLord", "MirEmperor", "MutantManEatingPlant", "MutantWarg", "GrassElemental",
        "RockElemental", "EvilMir", "EvilMirBody", "DragonStatue", "HellBomb1", "HellBomb2", "HellBomb3", "Catapult",
        "ChariotBallista", "Ballista", "Trebuchet", "CanonTrebuchet", "SabukGate", "PalaceWallLeft", "PalaceWall1", "PalaceWall2",
        "GiGateSouth", "GiGateEast", "GiGateWest", "SSabukWall1", "SSabukWall2", "SSabukWall3", "NammandGate1", "NammandGate2",
        "SabukWallSection", "NammandWallSection", "FrozenDoor", "BabyPig", "Chick", "Kitten", "BabySkeleton", "Baekdon",
        "Wimaen", "BlackKitten", "BabyDragon", "OlympicFlame", "BabySnowMan", "Frog", "BabyMonkey", "AngryBird",
        "Foxey", "MedicalRat");

    private static readonly IReadOnlyList<ContentEditorOptionDto> StatOptions = Options(
        ("MinAC", "最小防御"), ("MaxAC", "最大防御"), ("MinMAC", "最小魔御"), ("MaxMAC", "最大魔御"),
        ("MinDC", "最小攻击"), ("MaxDC", "最大攻击"), ("MinMC", "最小魔法"), ("MaxMC", "最大魔法"),
        ("MinSC", "最小道术"), ("MaxSC", "最大道术"), ("HP", "生命"), ("Accuracy", "准确"),
        ("Agility", "敏捷"));

    private static readonly HashSet<string> StatNames =
        StatOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);

    public static readonly ContentEditorSchemaDto Schema = new(
        EntityType,
        SchemaVersion,
        "怪物与 AI",
        "编辑 MonsterInfo 的身份、基础行为、战斗参数和固定属性。掉落文件与脚本标记保持运行时只读。",
        [
            new ContentEditorSectionDto("identity", "基础信息", "怪物身份、客户端图像和掉落文件路径。", [
                Field("Index", "旧索引", "integer", required: true, readOnly: true, defaultValue: null, min: 0, max: int.MaxValue),
                Field("Name", "名称", "string", required: true, defaultValue: ""),
                Field("Image", "怪物图像", "enum", required: true, defaultValue: "Guard", options: MonsterImages),
                Field("DropPath", "掉落路径", "string", required: true, defaultValue: "")
            ]),
            new ContentEditorSectionDto("basics", "基础行为", "等级、AI、特效、视野和感知参数。", [
                Field("AI", "AI", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("Effect", "特效", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("Level", "等级", "integer", required: true, defaultValue: 0, min: 0, max: ushort.MaxValue),
                Field("ViewRange", "视野范围", "integer", required: true, defaultValue: 7, min: 0, max: byte.MaxValue),
                Field("CoolEye", "反隐身", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue),
                Field("Light", "亮度", "integer", required: true, defaultValue: 0, min: 0, max: byte.MaxValue)
            ]),
            new ContentEditorSectionDto("combat", "战斗参数", "移动、攻击节奏和经验值。", [
                Field("AttackSpeed", "攻击速度", "integer", required: true, defaultValue: 2500, min: 0, max: ushort.MaxValue),
                Field("MoveSpeed", "移动速度", "integer", required: true, defaultValue: 1800, min: 0, max: ushort.MaxValue),
                Field("Experience", "经验值", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue)
            ]),
            new ContentEditorSectionDto("behavior", "行为开关", "驯服、推动、复活、亡灵、召回和 Boss 标记。", [
                Field("CanTame", "可驯服", "boolean", required: true, defaultValue: true),
                Field("CanPush", "可推动", "boolean", required: true, defaultValue: true),
                Field("AutoRev", "自动复活", "boolean", required: true, defaultValue: true),
                Field("Undead", "亡灵", "boolean", required: true, defaultValue: false),
                Field("CanRecall", "可召回", "boolean", required: true, defaultValue: false),
                Field("IsBoss", "Boss", "boolean", required: true, defaultValue: false)
            ]),
            new ContentEditorSectionDto("stats", "固定属性", "Stats.Values 中怪物的防御、攻击、生命和命中属性。", [
                Field("Stats", "属性值", "stat-map", required: true, defaultValue: new { Values = new SortedDictionary<string, int>(StringComparer.Ordinal) }, options: StatOptions)
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

        foreach (var field in TopLevelFields().Where(field => field.Kind != "stat-map"))
        {
            if (!data.TryGetProperty(field.Path, out var fieldValue)) continue;
            ValidateField(field, fieldValue, errors);
        }

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
            yield return "Monster data must be a JSON object.";
            yield break;
        }

        foreach (var field in TopLevelFields())
        {
            if (!data.TryGetProperty(field.Path, out _))
            {
                yield return $"Missing required monster field '{field.Path}'.";
            }
        }

        if (data.TryGetProperty("Name", out var name) &&
            name.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(name.GetString()))
        {
            yield return "Monster name must not be empty.";
        }

        if (data.TryGetProperty("Stats", out var stats))
        {
            if (stats.ValueKind != JsonValueKind.Object)
            {
                yield return "Monster field 'Stats' must be an object.";
            }
            else if (!stats.TryGetProperty("Values", out _))
            {
                yield return "Missing required monster field 'Stats.Values'.";
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
                errors[path] = [$"Unknown monster stat '{property.Name}'."];
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

    private static IReadOnlyList<ContentEditorOptionDto> EnumOptions(params string[] values)
    {
        return values.Select(value => new ContentEditorOptionDto(value, value)).ToArray();
    }
}
