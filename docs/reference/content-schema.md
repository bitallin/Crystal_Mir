# 内容字段清单（生成）

> 生成时间：2026-11-09 10:59:12 UTC
> 生成工具：`tools/Crystal.FieldInventory`
> 来源：`Server.Library` + `Shared`

本文件由反射生成，用于迁移和后台建模参考。字段名和类型以当前代码为准；修改实体后重新生成。

## 实体

## `ItemInfo`

- 类型：`ItemInfo`
- 程序集：`Shared`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `Bind` | `BindMode` |
| 字段 | `CanAwakening` | `Boolean` |
| 字段 | `CanFastRun` | `Boolean` |
| 字段 | `CanMine` | `Boolean` |
| 字段 | `ClassBased` | `Boolean` |
| 字段 | `Durability` | `UInt16` |
| 字段 | `Effect` | `Byte` |
| 只读属性 | `FriendlyName` | `string` |
| 字段 | `GlobalDropNotify` | `Boolean` |
| 字段 | `Grade` | `ItemGrade` |
| 字段 | `Image` | `UInt16` |
| 字段 | `Index` | `Int32` |
| 只读属性 | `IsConsumable` | `Boolean` |
| 只读属性 | `IsFishingRod` | `Boolean` |
| 字段 | `LevelBased` | `Boolean` |
| 字段 | `Light` | `Byte` |
| 字段 | `Name` | `string` |
| 字段 | `NeedIdentify` | `Boolean` |
| 字段 | `Price` | `UInt32` |
| 字段 | `RandomStats` | `RandomItemStat` |
| 字段 | `RandomStatsId` | `Byte` |
| 字段 | `RequiredAmount` | `Byte` |
| 字段 | `RequiredClass` | `RequiredClass` |
| 字段 | `RequiredGender` | `RequiredGender` |
| 字段 | `RequiredType` | `RequiredType` |
| 字段 | `Set` | `ItemSet` |
| 字段 | `Shape` | `Int16` |
| 字段 | `ShowGroupPickup` | `Boolean` |
| 字段 | `Slots` | `Byte` |
| 字段 | `StackSize` | `UInt16` |
| 字段 | `StartItem` | `Boolean` |
| 字段 | `Stats` | `Stats` |
| 字段 | `ToolTip` | `string` |
| 字段 | `Type` | `ItemType` |
| 字段 | `Unique` | `SpecialItemMode` |
| 字段 | `Weight` | `Byte` |

## `GameShopItem`

- 类型：`GameShopItem`
- 程序集：`Shared`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `CanBuyCredit` | `Boolean` |
| 字段 | `CanBuyGold` | `Boolean` |
| 字段 | `Category` | `string` |
| 字段 | `Class` | `string` |
| 字段 | `Count` | `UInt16` |
| 字段 | `CreditPrice` | `UInt32` |
| 字段 | `Date` | `DateTime` |
| 字段 | `Deal` | `Boolean` |
| 字段 | `GIndex` | `Int32` |
| 字段 | `GoldPrice` | `UInt32` |
| 字段 | `Info` | `ItemInfo` |
| 字段 | `ItemIndex` | `Int32` |
| 字段 | `Stock` | `Int32` |
| 字段 | `TopItem` | `Boolean` |
| 字段 | `iStock` | `Boolean` |

## `MonsterInfo`

- 类型：`Server.MirDatabase.MonsterInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `AI` | `Byte` |
| 字段 | `AttackSpeed` | `UInt16` |
| 字段 | `AutoRev` | `Boolean` |
| 字段 | `CanPush` | `Boolean` |
| 字段 | `CanRecall` | `Boolean` |
| 字段 | `CanTame` | `Boolean` |
| 只读属性 | `ClientInformation` | `ClientMonsterInfo` |
| 字段 | `CoolEye` | `Byte` |
| 字段 | `DropPath` | `string` |
| 字段 | `Drops` | `List<DropInfo>` |
| 字段 | `Effect` | `Byte` |
| 字段 | `Experience` | `UInt32` |
| 只读属性 | `GameName` | `string` |
| 字段 | `HasDieScript` | `Boolean` |
| 字段 | `HasSpawnScript` | `Boolean` |
| 字段 | `Image` | `Monster` |
| 字段 | `Index` | `Int32` |
| 字段 | `IsBoss` | `Boolean` |
| 字段 | `Level` | `UInt16` |
| 字段 | `Light` | `Byte` |
| 字段 | `MoveSpeed` | `UInt16` |
| 字段 | `Name` | `string` |
| 字段 | `Stats` | `Stats` |
| 字段 | `Undead` | `Boolean` |
| 字段 | `ViewRange` | `Byte` |

## `MagicInfo`

- 类型：`Server.MirDatabase.MagicInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `BaseCost` | `Byte` |
| 字段 | `DelayBase` | `UInt32` |
| 字段 | `DelayReduction` | `UInt32` |
| 字段 | `Icon` | `Byte` |
| 字段 | `Level1` | `Byte` |
| 字段 | `Level2` | `Byte` |
| 字段 | `Level3` | `Byte` |
| 字段 | `LevelCost` | `Byte` |
| 字段 | `MPowerBase` | `UInt16` |
| 字段 | `MPowerBonus` | `UInt16` |
| 字段 | `MultiplierBase` | `Single` |
| 字段 | `MultiplierBonus` | `Single` |
| 字段 | `Name` | `string` |
| 字段 | `Need1` | `UInt16` |
| 字段 | `Need2` | `UInt16` |
| 字段 | `Need3` | `UInt16` |
| 字段 | `PowerBase` | `UInt16` |
| 字段 | `PowerBonus` | `UInt16` |
| 字段 | `Range` | `Byte` |
| 字段 | `Spell` | `Spell` |

## `MapInfo`

- 类型：`Server.MirDatabase.MapInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `ActiveCoords` | `List<Point>` |
| 字段 | `BigMap` | `UInt16` |
| 字段 | `Fight` | `Boolean` |
| 字段 | `FileName` | `string` |
| 字段 | `Fire` | `Boolean` |
| 字段 | `FireDamage` | `Int32` |
| 字段 | `FireWallCount` | `Int32` |
| 字段 | `FireWallLimit` | `Boolean` |
| 字段 | `GT` | `Boolean` |
| 字段 | `GTIndex` | `Byte` |
| 字段 | `Index` | `Int32` |
| 字段 | `Light` | `LightSetting` |
| 字段 | `Lightning` | `Boolean` |
| 字段 | `LightningDamage` | `Int32` |
| 字段 | `MapDarkLight` | `Byte` |
| 字段 | `MineIndex` | `Byte` |
| 字段 | `MineZones` | `List<MineZone>` |
| 字段 | `MiniMap` | `UInt16` |
| 字段 | `Movements` | `List<MovementInfo>` |
| 字段 | `Music` | `UInt16` |
| 字段 | `NPCs` | `List<NPCInfo>` |
| 字段 | `NeedBridle` | `Boolean` |
| 字段 | `NeedHole` | `Boolean` |
| 字段 | `NoDropMonster` | `Boolean` |
| 字段 | `NoDropPlayer` | `Boolean` |
| 字段 | `NoDrug` | `Boolean` |
| 字段 | `NoEscape` | `Boolean` |
| 字段 | `NoExperience` | `Boolean` |
| 字段 | `NoFight` | `Boolean` |
| 字段 | `NoGroup` | `Boolean` |
| 字段 | `NoHero` | `Boolean` |
| 字段 | `NoIntelligentCreatures` | `Boolean` |
| 字段 | `NoMount` | `Boolean` |
| 字段 | `NoNames` | `Boolean` |
| 字段 | `NoPets` | `Boolean` |
| 字段 | `NoPosition` | `Boolean` |
| 字段 | `NoRandom` | `Boolean` |
| 字段 | `NoRecall` | `Boolean` |
| 字段 | `NoReconnect` | `Boolean` |
| 字段 | `NoReconnectMap` | `string` |
| 字段 | `NoReincarnation` | `Boolean` |
| 字段 | `NoTeleport` | `Boolean` |
| 字段 | `NoThrowItem` | `Boolean` |
| 字段 | `NoTownTeleport` | `Boolean` |
| 字段 | `RequiredGroup` | `Boolean` |
| 字段 | `RequiredGroupSize` | `Int32` |
| 字段 | `Respawns` | `List<RespawnInfo>` |
| 字段 | `SafeZones` | `List<SafeZoneInfo>` |
| 字段 | `Title` | `string` |
| 字段 | `WeatherParticles` | `WeatherSetting` |

## `NPCInfo`

- 类型：`Server.MirDatabase.NPCInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `BigMapIcon` | `Int32` |
| 字段 | `CanTeleportTo` | `Boolean` |
| 字段 | `ClassRequired` | `string` |
| 只读属性 | `ClientInformation` | `ClientNPCInfo` |
| 字段 | `CollectQuestIndexes` | `List<Int32>` |
| 字段 | `Colour` | `Color` |
| 字段 | `Conquest` | `Int32` |
| 字段 | `ConquestVisible` | `Boolean` |
| 字段 | `DayofWeek` | `string` |
| 字段 | `FileName` | `string` |
| 字段 | `FinishQuestIndexes` | `List<Int32>` |
| 字段 | `FlagNeeded` | `Int32` |
| 只读属性 | `GameName` | `string` |
| 字段 | `HourEnd` | `Byte` |
| 字段 | `HourStart` | `Byte` |
| 字段 | `Image` | `UInt16` |
| 字段 | `Index` | `Int32` |
| 字段 | `Location` | `Point` |
| 字段 | `MapIndex` | `Int32` |
| 字段 | `MaxLev` | `Int16` |
| 字段 | `MinLev` | `Int16` |
| 字段 | `MinuteEnd` | `Byte` |
| 字段 | `MinuteStart` | `Byte` |
| 字段 | `Name` | `string` |
| 字段 | `Rate` | `UInt16` |
| 字段 | `Sabuk` | `Boolean` |
| 字段 | `ShowOnBigMap` | `Boolean` |
| 字段 | `TimeVisible` | `Boolean` |

## `QuestInfo`

- 类型：`Server.MirDatabase.QuestInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `CarryItems` | `List<QuestItemTask>` |
| 字段 | `CompletionDescription` | `List<string>` |
| 字段 | `CreditReward` | `UInt32` |
| 字段 | `Description` | `List<string>` |
| 字段 | `ExpReward` | `UInt32` |
| 字段 | `FileName` | `string` |
| 只读属性 | `FinishNPC` | `NPCObject` |
| 属性 | `FinishNpcIndex` | `UInt32` |
| 字段 | `FixedRewards` | `List<QuestItemReward>` |
| 字段 | `FlagMessage` | `string` |
| 字段 | `FlagTasks` | `List<QuestFlagTask>` |
| 字段 | `GoldReward` | `UInt32` |
| 字段 | `GotoMessage` | `string` |
| 字段 | `Group` | `string` |
| 字段 | `Index` | `Int32` |
| 字段 | `ItemMessage` | `string` |
| 字段 | `ItemTasks` | `List<QuestItemTask>` |
| 字段 | `KillMessage` | `string` |
| 字段 | `KillTasks` | `List<QuestKillTask>` |
| 字段 | `Name` | `string` |
| 字段 | `NpcIndex` | `UInt32` |
| 字段 | `NpcInfo` | `NPCInfo` |
| 字段 | `RequiredClass` | `RequiredClass` |
| 字段 | `RequiredMaxLevel` | `Int32` |
| 字段 | `RequiredMinLevel` | `Int32` |
| 字段 | `RequiredQuest` | `Int32` |
| 字段 | `ReturnDescription` | `List<string>` |
| 字段 | `SelectRewards` | `List<QuestItemReward>` |
| 字段 | `TaskDescription` | `List<string>` |
| 字段 | `TimeLimitInSeconds` | `Int32` |
| 字段 | `Type` | `QuestType` |

## `RecipeInfo`

- 类型：`Server.MirDatabase.RecipeInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `Chance` | `Byte` |
| 字段 | `Gold` | `UInt32` |
| 字段 | `Ingredients` | `List<UserItem>` |
| 字段 | `Item` | `UserItem` |
| 字段 | `RequiredClass` | `List<MirClass>` |
| 字段 | `RequiredFlag` | `List<Int32>` |
| 字段 | `RequiredGender` | `Nullable<MirGender>` |
| 字段 | `RequiredLevel` | `Nullable<UInt16>` |
| 字段 | `RequiredQuest` | `List<Int32>` |
| 字段 | `Tools` | `List<UserItem>` |

## `BuffInfo`

- 类型：`Server.MirDatabase.BuffInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 属性 | `Icon` | `Int32` |
| 属性 | `Properties` | `BuffProperty` |
| 属性 | `StackType` | `BuffStackType` |
| 属性 | `Type` | `BuffType` |
| 属性 | `Visible` | `Boolean` |

## `ConquestInfo`

- 类型：`Server.MirDatabase.ConquestInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `ConquestFlags` | `List<ConquestFlagInfo>` |
| 字段 | `ConquestGates` | `List<ConquestGateInfo>` |
| 字段 | `ConquestGuards` | `List<ConquestArcherInfo>` |
| 字段 | `ConquestSieges` | `List<ConquestSiegeInfo>` |
| 字段 | `ConquestWalls` | `List<ConquestWallInfo>` |
| 字段 | `ControlPointIndex` | `Int32` |
| 字段 | `ControlPoints` | `List<ConquestFlagInfo>` |
| 字段 | `ExtraMaps` | `List<Int32>` |
| 字段 | `FlagIndex` | `Int32` |
| 字段 | `Friday` | `Boolean` |
| 字段 | `FullMap` | `Boolean` |
| 字段 | `Game` | `ConquestGame` |
| 字段 | `GateIndex` | `Int32` |
| 字段 | `GuardIndex` | `Int32` |
| 字段 | `Index` | `Int32` |
| 字段 | `KingLocation` | `Point` |
| 字段 | `KingSize` | `UInt16` |
| 字段 | `Location` | `Point` |
| 字段 | `MapIndex` | `Int32` |
| 字段 | `Monday` | `Boolean` |
| 字段 | `Name` | `string` |
| 字段 | `PalaceIndex` | `Int32` |
| 字段 | `Saturday` | `Boolean` |
| 字段 | `SiegeIndex` | `Int32` |
| 字段 | `Size` | `UInt16` |
| 字段 | `StartHour` | `Byte` |
| 字段 | `Sunday` | `Boolean` |
| 字段 | `Thursday` | `Boolean` |
| 字段 | `Tuesday` | `Boolean` |
| 字段 | `Type` | `ConquestType` |
| 字段 | `WallIndex` | `Int32` |
| 字段 | `WarLength` | `Int32` |
| 字段 | `Wednesday` | `Boolean` |

## `DragonInfo`

- 类型：`Server.MirDatabase.DragonInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `BodyName` | `string` |
| 字段 | `DropAreaBottom` | `Point` |
| 字段 | `DropAreaTop` | `Point` |
| 字段 | `Drops` | `List<DropInfo>[]` |
| 字段 | `Enabled` | `Boolean` |
| 字段 | `Experience` | `Int64` |
| 字段 | `Exps` | `Int64[]` |
| 字段 | `Level` | `Byte` |
| 字段 | `Location` | `Point` |
| 字段 | `MapFileName` | `string` |
| 字段 | `MonsterName` | `string` |

## `MineSet`

- 类型：`Server.MirDatabase.MineSet`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `DropRate` | `Byte` |
| 字段 | `Drops` | `List<MineDrop>` |
| 字段 | `HitRate` | `Byte` |
| 字段 | `MaxStones` | `Byte` |
| 字段 | `Name` | `string` |
| 字段 | `SpotRegenRate` | `Byte` |
| 字段 | `TotalSlots` | `Byte` |

## `RespawnInfo`

- 类型：`Server.MirDatabase.RespawnInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `Count` | `UInt16` |
| 字段 | `Delay` | `UInt16` |
| 字段 | `Direction` | `Byte` |
| 字段 | `Location` | `Point` |
| 字段 | `MonsterIndex` | `Int32` |
| 字段 | `RandomDelay` | `UInt16` |
| 字段 | `RespawnIndex` | `Int32` |
| 字段 | `RespawnTicks` | `UInt16` |
| 字段 | `RoutePath` | `string` |
| 字段 | `SaveRespawnTime` | `Boolean` |
| 字段 | `Spread` | `UInt16` |

## `SafeZoneInfo`

- 类型：`Server.MirDatabase.SafeZoneInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `Info` | `MapInfo` |
| 字段 | `Location` | `Point` |
| 字段 | `Size` | `UInt16` |
| 字段 | `StartPoint` | `Boolean` |

## `MovementInfo`

- 类型：`Server.MirDatabase.MovementInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `ConquestIndex` | `Int32` |
| 字段 | `Destination` | `Point` |
| 字段 | `Icon` | `Int32` |
| 字段 | `MapIndex` | `Int32` |
| 字段 | `NeedHole` | `Boolean` |
| 字段 | `NeedMove` | `Boolean` |
| 字段 | `ShowOnBigMap` | `Boolean` |
| 字段 | `Source` | `Point` |

## `AccountInfo`

- 类型：`Server.MirDatabase.AccountInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `AccountID` | `string` |
| 字段 | `AdminAccount` | `Boolean` |
| 字段 | `Auctions` | `LinkedList<AuctionInfo>` |
| 字段 | `BanReason` | `string` |
| 字段 | `Banned` | `Boolean` |
| 字段 | `BirthDate` | `DateTime` |
| 字段 | `Characters` | `List<CharacterInfo>` |
| 字段 | `Connection` | `MirConnection` |
| 字段 | `CreationDate` | `DateTime` |
| 字段 | `CreationIP` | `string` |
| 字段 | `Credit` | `UInt32` |
| 字段 | `EMailAddress` | `string` |
| 字段 | `ExpandedStorageExpiryDate` | `DateTime` |
| 字段 | `ExpiryDate` | `DateTime` |
| 字段 | `Gold` | `UInt32` |
| 字段 | `HasExpandedStorage` | `Boolean` |
| 只读属性 | `HasStoragePassword` | `Boolean` |
| 字段 | `Index` | `Int32` |
| 字段 | `LastDate` | `DateTime` |
| 字段 | `LastIP` | `string` |
| 属性 | `Password` | `string` |
| 字段 | `RequirePasswordChange` | `Boolean` |
| 字段 | `Salt` | `Byte[]` |
| 字段 | `SecretAnswer` | `string` |
| 字段 | `SecretQuestion` | `string` |
| 字段 | `Storage` | `UserItem[]` |
| 属性 | `StoragePassword` | `string` |
| 字段 | `StoragePasswordLastSet` | `DateTime` |
| 字段 | `StorageSalt` | `Byte[]` |
| 字段 | `UserName` | `string` |
| 字段 | `WrongPasswordCount` | `Int32` |

## `CharacterInfo`

- 类型：`Server.MirDatabase.CharacterInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `AMode` | `AttackMode` |
| 字段 | `AccountInfo` | `AccountInfo` |
| 字段 | `AllowGroup` | `Boolean` |
| 字段 | `AllowObserve` | `Boolean` |
| 字段 | `AllowTrade` | `Boolean` |
| 字段 | `BanReason` | `string` |
| 字段 | `Banned` | `Boolean` |
| 字段 | `BindLocation` | `Point` |
| 字段 | `BindMapIndex` | `Int32` |
| 字段 | `Buffs` | `List<Buff>` |
| 字段 | `ChatBanExpiryDate` | `DateTime` |
| 字段 | `ChatBanned` | `Boolean` |
| 字段 | `Class` | `MirClass` |
| 字段 | `CollectTime` | `Int64` |
| 字段 | `CompletedQuests` | `List<Int32>` |
| 字段 | `CreationDate` | `DateTime` |
| 字段 | `CreationIP` | `string` |
| 字段 | `CrossHalfMoon` | `Boolean` |
| 字段 | `CurrentHeroIndex` | `Int32` |
| 字段 | `CurrentLocation` | `Point` |
| 字段 | `CurrentMapIndex` | `Int32` |
| 字段 | `CurrentQuests` | `List<QuestProgressInfo>` |
| 字段 | `CurrentRefine` | `UserItem` |
| 字段 | `DeleteDate` | `DateTime` |
| 字段 | `Deleted` | `Boolean` |
| 字段 | `Direction` | `MirDirection` |
| 字段 | `DoubleSlash` | `Boolean` |
| 字段 | `Equipment` | `UserItem[]` |
| 字段 | `Experience` | `Int64` |
| 字段 | `ExpiryDate` | `DateTime` |
| 字段 | `Flags` | `Boolean[]` |
| 字段 | `Friends` | `List<FriendInfo>` |
| 字段 | `GSpurchases` | `Dictionary<Int32, Int32>` |
| 字段 | `Gender` | `MirGender` |
| 字段 | `GuildIndex` | `Int32` |
| 字段 | `HP` | `Int32` |
| 字段 | `Hair` | `Byte` |
| 字段 | `HalfMoon` | `Boolean` |
| 字段 | `HasRentedItem` | `Boolean` |
| 字段 | `HeroBehaviour` | `HeroBehaviour` |
| 字段 | `HeroSpawned` | `Boolean` |
| 字段 | `Heroes` | `HeroInfo[]` |
| 字段 | `Index` | `Int32` |
| 字段 | `IntelligentCreatures` | `List<UserIntelligentCreature>` |
| 字段 | `Inventory` | `UserItem[]` |
| 字段 | `IsMentor` | `Boolean` |
| 字段 | `LastIP` | `string` |
| 字段 | `LastLoginDate` | `DateTime` |
| 字段 | `LastLogoutDate` | `DateTime` |
| 字段 | `Level` | `UInt16` |
| 字段 | `MP` | `Int32` |
| 字段 | `Magics` | `List<UserMagic>` |
| 字段 | `Mail` | `List<MailInfo>` |
| 字段 | `Married` | `Int32` |
| 字段 | `MarriedDate` | `DateTime` |
| 字段 | `MaximumHeroCount` | `Int32` |
| 字段 | `MentalState` | `Byte` |
| 字段 | `MentalStateLvl` | `Byte` |
| 字段 | `Mentor` | `Int32` |
| 字段 | `MentorDate` | `DateTime` |
| 字段 | `MentorExp` | `Int64` |
| 字段 | `Mount` | `MountInfo` |
| 字段 | `Name` | `string` |
| 字段 | `NewDay` | `Boolean` |
| 字段 | `PKPoints` | `Int32` |
| 字段 | `PMode` | `PetMode` |
| 字段 | `PearlCount` | `Int32` |
| 字段 | `Pets` | `List<PetInfo>` |
| 字段 | `Player` | `PlayerObject` |
| 字段 | `Poisons` | `List<Poison>` |
| 字段 | `QuestInventory` | `UserItem[]` |
| 字段 | `Rank` | `Int32[]` |
| 字段 | `Refine` | `UserItem[]` |
| 字段 | `RefineTimeRemaining` | `Int64` |
| 字段 | `RentedItems` | `List<ItemRentalInformation>` |
| 字段 | `RentedItemsToRemove` | `List<ItemRentalInformation>` |
| 字段 | `Thrusting` | `Boolean` |
| 字段 | `Trade` | `UserItem[]` |

## `GuildInfo`

- 类型：`Server.MirDatabase.GuildInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `BuffList` | `List<GuildBuff>` |
| 字段 | `Experience` | `Int64` |
| 字段 | `FlagColour` | `Color` |
| 字段 | `FlagImage` | `UInt16` |
| 字段 | `GTBegin` | `DateTime` |
| 字段 | `GTIndex` | `Int32` |
| 字段 | `GTKey` | `Int32` |
| 字段 | `GTPrice` | `Int32` |
| 字段 | `GTRent` | `DateTime` |
| 字段 | `Gold` | `UInt32` |
| 字段 | `GuildIndex` | `Int32` |
| 只读属性 | `HasGT` | `Boolean` |
| 字段 | `LastVoteAttempt` | `DateTime` |
| 字段 | `Level` | `Byte` |
| 字段 | `MaxExperience` | `Int64` |
| 字段 | `MemberCap` | `Int32` |
| 字段 | `Membercount` | `Int32` |
| 字段 | `Name` | `string` |
| 字段 | `NeedSave` | `Boolean` |
| 字段 | `Notice` | `List<string>` |
| 字段 | `Ranks` | `List<GuildRank>` |
| 字段 | `SparePoints` | `Byte` |
| 字段 | `StoredItems` | `GuildStorageItem[]` |
| 字段 | `Votes` | `Int32` |
| 字段 | `Voting` | `Boolean` |

## `HeroInfo`

- 类型：`Server.MirDatabase.HeroInfo`
- 程序集：`Server.Library`
- 基类：`CharacterInfo`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `AutoHPPercent` | `Byte` |
| 字段 | `AutoMPPercent` | `Byte` |
| 字段 | `AutoPot` | `Boolean` |
| 只读属性 | `ClientInformation` | `ClientHeroInformation` |
| 字段 | `Grade` | `Byte` |
| 字段 | `HPItemIndex` | `Int32` |
| 字段 | `MPItemIndex` | `Int32` |
| 字段 | `SealCount` | `UInt16` |

## `IntelligentCreatureInfo`

- 类型：`Server.MirDatabase.IntelligentCreatureInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `AutoPickupEnabled` | `Boolean` |
| 字段 | `AutoPickupRange` | `Int32` |
| 字段 | `CanProduceBlackStone` | `Boolean` |
| 字段 | `Icon` | `Int32` |
| 字段 | `MinimalFullness` | `Int32` |
| 字段 | `MousePickupEnabled` | `Boolean` |
| 字段 | `MousePickupRange` | `Int32` |
| 字段 | `PetType` | `IntelligentCreatureType` |
| 字段 | `SemiAutoPickupEnabled` | `Boolean` |
| 字段 | `SemiAutoPickupRange` | `Int32` |

## `MailInfo`

- 类型：`Server.MirEnvir.MailInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `CanReply` | `Boolean` |
| 字段 | `Collected` | `Boolean` |
| 字段 | `DateOpened` | `DateTime` |
| 字段 | `DateSent` | `DateTime` |
| 字段 | `Gold` | `UInt32` |
| 字段 | `Items` | `List<UserItem>` |
| 字段 | `Locked` | `Boolean` |
| 字段 | `MailID` | `UInt64` |
| 字段 | `Message` | `string` |
| 只读属性 | `Opened` | `Boolean` |
| 只读属性 | `Parcel` | `Boolean` |
| 字段 | `RecipientIndex` | `Int32` |
| 字段 | `RecipientInfo` | `CharacterInfo` |
| 字段 | `Sender` | `string` |
| 只读属性 | `Sent` | `Boolean` |

## `AuctionInfo`

- 类型：`Server.MirDatabase.AuctionInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `AuctionID` | `UInt64` |
| 字段 | `ConsignmentDate` | `DateTime` |
| 字段 | `CurrentBid` | `UInt32` |
| 字段 | `CurrentBuyerIndex` | `Int32` |
| 字段 | `CurrentBuyerInfo` | `CharacterInfo` |
| 字段 | `Expired` | `Boolean` |
| 字段 | `Item` | `UserItem` |
| 字段 | `ItemType` | `MarketItemType` |
| 字段 | `Price` | `UInt32` |
| 字段 | `SellerIndex` | `Int32` |
| 字段 | `SellerInfo` | `CharacterInfo` |
| 字段 | `Sold` | `Boolean` |

## `GTMap`

- 类型：`Server.Library.MirDatabase.GTMap`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `Begin` | `Int32` |
| 字段 | `Days` | `Int32` |
| 字段 | `Index` | `Int32` |
| 字段 | `Key` | `Int32` |
| 字段 | `Leader` | `string` |
| 字段 | `Leader2` | `string` |
| 字段 | `Maps` | `List<Map>` |
| 字段 | `Name` | `string` |
| 字段 | `Owner` | `string` |
| 字段 | `Price` | `Int32` |

## `QuestProgressInfo`

- 类型：`Server.MirDatabase.QuestProgressInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 只读属性 | `Completed` | `Boolean` |
| 字段 | `EndDateTime` | `DateTime` |
| 字段 | `FlagTaskSet` | `List<QuestFlagTaskProgress>` |
| 字段 | `Index` | `Int32` |
| 字段 | `Info` | `QuestInfo` |
| 只读属性 | `IsOrphan` | `Boolean` |
| 字段 | `ItemTaskCount` | `List<QuestItemTaskProgress>` |
| 字段 | `KillTaskCount` | `List<QuestKillTaskProgress>` |
| 只读属性 | `New` | `Boolean` |
| 字段 | `Owner` | `PlayerObject` |
| 字段 | `StartDateTime` | `DateTime` |
| 只读属性 | `Taken` | `Boolean` |
| 字段 | `TaskList` | `List<string>` |

## `ConquestGuildInfo`

- 类型：`Server.MirDatabase.ConquestGuildInfo`
- 程序集：`Server.Library`
- 基类：`Object`

| 种类 | 字段/属性 | 类型 |
| --- | --- | --- |
| 字段 | `ArcherList` | `List<ConquestGuildArcherInfo>` |
| 字段 | `AttackerID` | `Int32` |
| 字段 | `ControlPoints` | `Dictionary<ConquestGuildFlagInfo, Dictionary<GuildObject, Int32>>` |
| 字段 | `FlagList` | `List<ConquestGuildFlagInfo>` |
| 字段 | `GateList` | `List<ConquestGuildGateInfo>` |
| 字段 | `GoldStorage` | `UInt32` |
| 字段 | `Info` | `ConquestInfo` |
| 字段 | `NPCRate` | `Byte` |
| 字段 | `NeedSave` | `Boolean` |
| 字段 | `Owner` | `Int32` |
| 字段 | `SiegeList` | `List<ConquestGuildSiegeInfo>` |
| 字段 | `WallList` | `List<ConquestGuildWallInfo>` |
