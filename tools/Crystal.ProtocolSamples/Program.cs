using System.Drawing;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

string output = args.Length > 0 ? args[0] : "tests/fixtures/protocol/cases.json";
List<SampleCase> cases = [];

void Add(string name, string direction, Packet packet, object? summary = null)
{
    byte[] bytes = packet.GetPacketBytes().ToArray();
    cases.Add(new SampleCase
    {
        Name = name,
        Direction = direction,
        Id = packet.Index,
        Hex = Convert.ToHexString(bytes).ToLowerInvariant(),
        Bytes = bytes.Length,
        Compressed = packet.Compressed,
        Summary = summary,
    });
}

DateTime fixedDate = new(2026, 11, 19, 10, 30, 0, DateTimeKind.Utc);

Add("client.version", "client", new C.ClientVersion { VersionHash = [0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef] });
Add("client.keepalive", "client", new C.KeepAlive { Time = 1_763_548_200_000 });
Add("client.login", "client", new C.Login { AccountID = "player.一", Password = "crystal-pass" });
Add("client.newCharacter", "client", new C.NewCharacter { Name = "测试角色", Gender = MirGender.Female, Class = MirClass.Taoist });
Add("client.deleteCharacter", "client", new C.DeleteCharacter { CharacterIndex = 3 });
Add("client.startGame", "client", new C.StartGame { CharacterIndex = 2 });
Add("client.logout", "client", new C.LogOut());
Add("client.turn", "client", new C.Turn { Direction = MirDirection.DownRight });
Add("client.walk", "client", new C.Walk { Direction = MirDirection.Left });
Add("client.run", "client", new C.Run { Direction = MirDirection.UpRight });
Add("client.chat", "client", new C.Chat { Message = "你好，Crystal!" });
Add("client.moveItem", "client", new C.MoveItem { Grid = MirGridType.Inventory, From = 2, To = 9 });
Add("client.attack", "client", new C.Attack { Direction = MirDirection.Right, Spell = Spell.None });
Add("client.pickup", "client", new C.PickUp());
Add("client.requestItemInfo", "client", new C.RequestItemInfo { ItemIndex = 101 });
Add("client.callNpc", "client", new C.CallNPC { ObjectID = 0x01020304, Key = "@main" });
Add("client.buyItem", "client", new C.BuyItem { ItemIndex = 0x0102030405060708, Count = 5, Type = PanelType.Buy });
Add("client.sellItem", "client", new C.SellItem { UniqueID = 0x8877665544332211, Count = 2 });
Add("client.useItem", "client", new C.UseItem { UniqueID = 0x1234567890abcdef, Grid = MirGridType.Inventory });
Add("client.equipItem", "client", new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = 0x0f1e2d3c4b5a6978, To = 4 });
Add("client.removeItem", "client", new C.RemoveItem { Grid = MirGridType.Equipment, UniqueID = 0x0f1e2d3c4b5a6978, To = 2 });
Add("client.dropItem", "client", new C.DropItem { UniqueID = 0xfedcba9876543210, Count = 1 });
Add("client.townRevive", "client", new C.TownRevive());

UserItem Item(ulong id, int index, ushort count = 1)
{
    ItemInfo info = new() { Index = index };
    return new UserItem(info)
    {
        UniqueID = id,
        ItemIndex = index,
        CurrentDura = 900,
        MaxDura = 1000,
        Count = count,
        SoulBoundId = -1,
        Identified = true,
        Cursed = false,
        Slots = [],
        GemCount = 0,
        AddedStats = new Stats(),
        Awake = new Awake(),
        RefinedValue = RefinedValue.None,
        RefineAdded = 0,
        RefineSuccessChance = 0,
        WeddingRing = -1,
        IsShopItem = false,
        GMMade = false,
    };
}

SelectInfo Character(int index, string name, ushort level, MirClass mirClass, MirGender gender)
{
    return new SelectInfo
    {
        Index = index,
        Name = name,
        Level = level,
        Class = mirClass,
        Gender = gender,
        LastAccess = fixedDate,
    };
}

Add("server.connected", "server", new S.Connected());
Add("server.clientVersion", "server", new S.ClientVersion { Result = 1 });
Add("server.disconnect", "server", new S.Disconnect { Reason = 1 });
Add("server.keepalive", "server", new S.KeepAlive { Time = 1_763_548_200_000 });
Add("server.login", "server", new S.Login { Result = 4 });
Add("server.loginBanned", "server", new S.LoginBanned { Reason = "临时封禁", ExpiryDate = fixedDate.AddHours(2) });
Add("server.loginSuccess", "server", new S.LoginSuccess
{
    Characters =
    [
        Character(0, "凌云", 42, MirClass.Warrior, MirGender.Male),
        Character(1, "青岚", 37, MirClass.Wizard, MirGender.Female),
    ]
}, new { characterCount = 2, firstCharacter = "凌云" });
Add("server.newCharacter", "server", new S.NewCharacter { Result = 10 });
Add("server.newCharacterSuccess", "server", new S.NewCharacterSuccess
{
    CharInfo = Character(2, "新角色", 1, MirClass.Archer, MirGender.Female)
});
Add("server.deleteCharacter", "server", new S.DeleteCharacter { Result = 1 });
Add("server.deleteCharacterSuccess", "server", new S.DeleteCharacterSuccess { CharacterIndex = 2 });
Add("server.startGame", "server", new S.StartGame { Result = 0, Resolution = 1920 });
Add("server.startGameBanned", "server", new S.StartGameBanned { Reason = "禁止进入", ExpiryDate = fixedDate.AddDays(1) });
Add("server.startGameDelay", "server", new S.StartGameDelay { Milliseconds = 750 });
Add("server.mapInformation", "server", new S.MapInformation
{
    MapIndex = 7,
    FileName = "0",
    Title = "比奇省",
    MiniMap = 11,
    BigMap = 12,
    Lights = LightSetting.Day,
    Lightning = true,
    Fire = false,
    MapDarkLight = 3,
    Music = 4,
    WeatherParticles = WeatherSetting.Snow,
}, new { mapIndex = 7, fileName = "0", title = "比奇省" });
Add("server.newMapInfo", "server", new S.NewMapInfo
{
    MapIndex = 7,
    Info = new ClientMapInfo
    {
        Title = "比奇省",
        Width = 320,
        Height = 240,
        BigMap = 9,
        Movements =
        [
            new ClientMovementInfo
            {
                Destination = 8,
                Title = "边界村",
                Location = new Point(100, 200),
                Icon = 2,
            },
        ],
        NPCs =
        [
            new ClientNPCInfo
            {
                Index = 10,
                FileName = "BorderVillage/Guard",
                Name = "守卫",
                MapIndex = 7,
                Location = new Point(55, 66),
                Image = 77,
                Rate = 100,
                ShowOnBigMap = true,
                BigMapIcon = 3,
                ObjectID = 0x0000aabb,
                Icon = 3,
                CanTeleportTo = true,
            },
        ],
    },
});

Add("server.userInformation", "server", new S.UserInformation
{
    ObjectID = 1001,
    RealId = 2002,
    Name = "凌云",
    GuildName = "行会",
    GuildRank = "成员",
    NameColour = Color.FromArgb(unchecked((int)0xffffd7a0)),
    Class = MirClass.Warrior,
    Gender = MirGender.Male,
    Level = 42,
    Location = new Point(10, 20),
    Direction = MirDirection.Down,
    Hair = 3,
    HP = 300,
    MP = 120,
    Experience = 123456789,
    MaxExperience = 987654321,
    LevelEffects = LevelEffects.RedDragon,
    HasHero = false,
    HeroBehaviour = HeroBehaviour.Follow,
    Inventory = [Item(1, 100, 3), null],
    Equipment = [null, Item(2, 200)],
    QuestInventory = null,
    Gold = 5000,
    Credit = 25,
    HasExpandedStorage = false,
    HasStoragePassword = false,
    RequireStoragePassword = false,
    StoragePasswordLastSet = default,
    ExpandedStorageExpiryTime = default,
    Magics =
    [
        new ClientMagic
        {
            Name = "基本剑术",
            Spell = Spell.Fencing,
            BaseCost = 0,
            LevelCost = 1,
            Icon = 2,
            Level1 = 1,
            Level2 = 2,
            Level3 = 3,
            Need1 = 100,
            Need2 = 200,
            Need3 = 300,
            Level = 3,
            Key = 1,
            Range = 1,
            Experience = 42,
            Delay = 250,
            CastTime = 500,
        },
    ],
    IntelligentCreatures = [],
}, new { name = "凌云", level = 42, inventoryCount = 2 });
Add("server.userSlotsRefresh", "server", new S.UserSlotsRefresh
{
    Inventory = [Item(3, 300, 7), null],
    Equipment = [Item(4, 400), null],
});
Add("server.userLocation", "server", new S.UserLocation { Location = new Point(31, 41), Direction = MirDirection.Left });
Add("server.objectPlayer", "server", new S.ObjectPlayer
{
    ObjectID = 5001,
    Name = "凌云",
    GuildName = "行会",
    GuildRankName = "成员",
    NameColour = Color.FromArgb(unchecked((int)0xffe8d7b0)),
    Class = MirClass.Warrior,
    Gender = MirGender.Male,
    Level = 42,
    Location = new Point(12, 34),
    Direction = MirDirection.DownRight,
    Hair = 2,
    Light = 4,
    Weapon = 5,
    WeaponEffect = 6,
    Armour = 7,
    Poison = PoisonType.None,
    Dead = false,
    Hidden = false,
    Effect = SpellEffect.None,
    WingEffect = 0,
    Extra = false,
    MountType = -1,
    RidingMount = false,
    Fishing = false,
    TransformType = 0,
    ElementOrbEffect = 0,
    ElementOrbLvl = 0,
    ElementOrbMax = 0,
    Buffs = [BuffType.Haste],
    LevelEffects = LevelEffects.None,
}, new { name = "凌云", objectId = 5001 });
Add("server.objectHero", "server", new S.ObjectHero
{
    ObjectID = 5002,
    Name = "英雄",
    GuildName = "",
    GuildRankName = "",
    NameColour = Color.White,
    Class = MirClass.Taoist,
    Gender = MirGender.Female,
    Level = 35,
    Location = new Point(13, 35),
    Direction = MirDirection.Up,
    Hair = 1,
    Light = 2,
    Weapon = -1,
    WeaponEffect = -1,
    Armour = 8,
    Poison = PoisonType.None,
    Dead = false,
    Hidden = false,
    Effect = SpellEffect.None,
    WingEffect = 0,
    Extra = false,
    MountType = -1,
    RidingMount = false,
    Fishing = false,
    TransformType = 0,
    ElementOrbEffect = 0,
    ElementOrbLvl = 0,
    ElementOrbMax = 0,
    Buffs = [],
    LevelEffects = LevelEffects.None,
    OwnerName = "凌云",
});
Add("server.objectRemove", "server", new S.ObjectRemove { ObjectID = 5001 });
Add("server.objectTurn", "server", new S.ObjectTurn { ObjectID = 5001, Location = new Point(12, 34), Direction = MirDirection.Right });
Add("server.objectWalk", "server", new S.ObjectWalk { ObjectID = 5001, Location = new Point(13, 34), Direction = MirDirection.Right });
Add("server.objectRun", "server", new S.ObjectRun { ObjectID = 5001, Location = new Point(15, 34), Direction = MirDirection.Right });
Add("server.chat", "server", new S.Chat { Message = "欢迎来到比奇省", Type = ChatType.System });
Add("server.objectChat", "server", new S.ObjectChat { ObjectID = 5001, Text = "组队吗？", Type = ChatType.Normal });
Add("server.moveItem", "server", new S.MoveItem { Grid = MirGridType.Inventory, From = 2, To = 9, Success = true });
Add("server.newItemInfo", "server", new S.NewItemInfo
{
    Info = new ItemInfo
    {
        Index = 101,
        Name = "金创药（小量）",
        Type = ItemType.Potion,
        Grade = ItemGrade.Common,
        RequiredType = RequiredType.Level,
        RequiredClass = RequiredClass.None,
        RequiredGender = RequiredGender.None,
        Set = ItemSet.None,
        Shape = 1,
        Weight = 1,
        Light = 1,
        RequiredAmount = 1,
        Image = 101,
        Durability = 0,
        StackSize = 30,
        Price = 25,
        StartItem = true,
        Effect = 1,
        NeedIdentify = false,
        ShowGroupPickup = false,
        ClassBased = false,
        LevelBased = false,
        CanMine = false,
        GlobalDropNotify = false,
        Bind = BindMode.None,
        Unique = SpecialItemMode.None,
        RandomStatsId = 0,
        CanFastRun = false,
        CanAwakening = false,
        Slots = 0,
        ToolTip = "恢复少量生命值。",
    }
}, new { index = 101, name = "金创药（小量）" });
Add("server.objectItem", "server", new S.ObjectItem
{
    ObjectID = 6001,
    Name = "金创药",
    NameColour = Color.White,
    Location = new Point(14, 36),
    Image = 101,
    grade = ItemGrade.Common,
});
Add("server.objectGold", "server", new S.ObjectGold { ObjectID = 6002, Gold = 250, Location = new Point(15, 37) });
Add("server.objectMonster", "server", new S.ObjectMonster
{
    ObjectID = 7001,
    Name = "鸡",
    NameColour = Color.White,
    Location = new Point(16, 38),
    Image = Monster.Hen,
    Direction = MirDirection.Left,
    Effect = 0,
    AI = 1,
    Light = 0,
    Dead = false,
    Skeleton = false,
    Poison = PoisonType.None,
    Hidden = false,
    ShockTime = 0,
    BindingShotCenter = false,
    Extra = false,
    ExtraByte = 0,
    MasterObjectId = 0,
    Rarity = MonsterType.Normal,
    Buffs = [],
}, new { name = "鸡", image = (ushort)Monster.Hen });
Add("server.objectNpc", "server", new S.ObjectNPC
{
    ObjectID = 8001,
    Name = "守卫",
    NameColour = Color.Yellow,
    Image = 77,
    Colour = Color.White,
    Location = new Point(17, 39),
    Direction = MirDirection.Up,
    QuestIDs = [1, 2],
});
Add("server.npcResponse", "server", new S.NPCResponse { Page = ["<main>", "欢迎。", "{@buy}"] });
Add("server.npcGoods", "server", new S.NPCGoods
{
    List = [Item(9, 900, 5)],
    Rate = 1.25f,
    Type = PanelType.Buy,
    HideAddedStats = false,
}, new { itemCount = 1, panelType = 0 });
Add("server.gainedItem", "server", new S.GainedItem { Item = Item(11, 1100, 2) });
Add("server.gainedGold", "server", new S.GainedGold { Gold = 500 });
Add("server.loseGold", "server", new S.LoseGold { Gold = 30 });
Add("server.deleteItem", "server", new S.DeleteItem { UniqueID = 11, Count = 1 });
Add("server.durabilityChanged", "server", new S.DuraChanged { UniqueID = 11, CurrentDura = 800 });
Add("server.healthChanged", "server", new S.HealthChanged { HP = 280, MP = 90 });
Add("server.damageIndicator", "server", new S.DamageIndicator { Damage = 37, Type = DamageType.Critical, ObjectID = 7001 });
Add("server.struck", "server", new S.Struck { AttackerID = 7001 });
Add("server.objectStruck", "server", new S.ObjectStruck { ObjectID = 5001, AttackerID = 7001, Location = new Point(12, 34), Direction = MirDirection.Down });
Add("server.death", "server", new S.Death { Location = new Point(12, 34), Direction = MirDirection.Down });
Add("server.objectDied", "server", new S.ObjectDied { ObjectID = 7001, Location = new Point(16, 38), Direction = MirDirection.Left, Type = 0 });
Add("server.revived", "server", new S.Revived());
Add("server.objectRevived", "server", new S.ObjectRevived { ObjectID = 5001, Effect = true });
Add("server.mapChanged", "server", new S.MapChanged
{
    MapIndex = 7,
    FileName = "0",
    Title = "",
    MiniMap = 0,
    BigMap = 0,
    Lights = LightSetting.Normal,
    Location = new Point(12, 34),
    Direction = MirDirection.Down,
    MapDarkLight = 0,
    Music = 0,
    Weather = WeatherSetting.None,
});
Add("server.objectHide", "server", new S.ObjectHide { ObjectID = 5001 });
Add("server.objectShow", "server", new S.ObjectShow { ObjectID = 5001 });
Add("server.objectTeleportOut", "server", new S.ObjectTeleportOut { ObjectID = 5001, Type = 1 });
Add("server.objectTeleportIn", "server", new S.ObjectTeleportIn { ObjectID = 5001, Type = 1 });
Add("server.teleportIn", "server", new S.TeleportIn());
Add("server.objectHealth", "server", new S.ObjectHealth { ObjectID = 7001, Percent = 64, Expire = 0 });
Add("server.objectMana", "server", new S.ObjectMana { ObjectID = 5001, Percent = 75 });
Add("server.objectLeveled", "server", new S.ObjectLeveled { ObjectID = 5001 });
Add("server.levelChanged", "server", new S.LevelChanged { Level = 43, Experience = 42, MaxExperience = 900 });
Add("server.gainExperience", "server", new S.GainExperience { Amount = 1200 });
Add("server.timeOfDay", "server", new S.TimeOfDay { Lights = LightSetting.Evening });
Add("server.logoutSuccess", "server", new S.LogOutSuccess());
Add("server.logoutFailed", "server", new S.LogOutFailed());
Add("server.returnToLogin", "server", new S.ReturnToLogin());

string? directory = Path.GetDirectoryName(Path.GetFullPath(output));
if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
File.WriteAllText(output, JsonSerializer.Serialize(new FixtureFile
{
    SchemaVersion = 1,
    Source = "Shared packet classes",
    Cases = cases,
}, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
}));
Console.WriteLine($"Wrote {cases.Count} protocol samples to {Path.GetFullPath(output)}");

internal sealed class SampleCase
{
    public required string Name { get; init; }
    public required string Direction { get; init; }
    public required int Id { get; init; }
    public required string Hex { get; init; }
    public required int Bytes { get; init; }
    public required bool Compressed { get; init; }
    public object? Summary { get; init; }
}

internal sealed class FixtureFile
{
    public required int SchemaVersion { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<SampleCase> Cases { get; init; }
}
