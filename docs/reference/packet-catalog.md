# 协议包目录（生成）

> 生成时间：2026-11-09 10:59:12 UTC
> 生成工具：`tools/Crystal.FieldInventory`
> 来源：`Shared/Packet.cs`、`Shared/ClientPackets.cs`、`Shared/ServerPackets.cs`

本文件由反射生成。`Index` 来自各包类的 `Index` 属性；字段列表不包含 `Index` 和继承成员。

## 客户端 -> 服务端

| Index | Packet | 字段 |
| --- | --- | --- |
| 0 | `ClientVersion` | VersionHash:Byte[] |
| 1 | `Disconnect` | - |
| 2 | `KeepAlive` | Time:Int64 |
| 3 | `NewAccount` | AccountID:string, BirthDate:DateTime, EMailAddress:string, Password:string, SecretAnswer:string, SecretQuestion:string, UserName:string |
| 4 | `ChangePassword` | AccountID:string, CurrentPassword:string, NewPassword:string |
| 5 | `Login` | AccountID:string, Password:string |
| 6 | `NewCharacter` | Class:MirClass, Gender:MirGender, Name:string |
| 7 | `DeleteCharacter` | CharacterIndex:Int32 |
| 8 | `StartGame` | CharacterIndex:Int32 |
| 9 | `LogOut` | - |
| 10 | `Turn` | Direction:MirDirection |
| 11 | `Walk` | Direction:MirDirection |
| 12 | `Run` | Direction:MirDirection |
| 13 | `Chat` | LinkedItems:List<ChatItem>, Message:string |
| 14 | `MoveItem` | From:Int32, Grid:MirGridType, To:Int32 |
| 15 | `StoreItem` | From:Int32, To:Int32 |
| 16 | `TakeBackItem` | From:Int32, To:Int32 |
| 17 | `MergeItem` | GridFrom:MirGridType, GridTo:MirGridType, IDFrom:UInt64, IDTo:UInt64 |
| 18 | `EquipItem` | Grid:MirGridType, To:Int32, UniqueID:UInt64 |
| 19 | `RemoveItem` | Grid:MirGridType, To:Int32, UniqueID:UInt64 |
| 20 | `RemoveSlotItem` | FromUniqueID:UInt64, Grid:MirGridType, GridTo:MirGridType, To:Int32, UniqueID:UInt64 |
| 21 | `SplitItem` | Count:UInt16, Grid:MirGridType, UniqueID:UInt64 |
| 22 | `UseItem` | Grid:MirGridType, UniqueID:UInt64 |
| 23 | `DropItem` | Count:UInt16, HeroInventory:Boolean, UniqueID:UInt64 |
| 24 | `DepositRefineItem` | From:Int32, To:Int32 |
| 25 | `RetrieveRefineItem` | From:Int32, To:Int32 |
| 26 | `RefineCancel` | - |
| 27 | `RefineItem` | UniqueID:UInt64 |
| 28 | `CheckRefine` | UniqueID:UInt64 |
| 29 | `ReplaceWedRing` | UniqueID:UInt64 |
| 30 | `DepositTradeItem` | From:Int32, To:Int32 |
| 31 | `RetrieveTradeItem` | From:Int32, To:Int32 |
| 32 | `TakeBackHeroItem` | From:Int32, To:Int32 |
| 33 | `TransferHeroItem` | From:Int32, To:Int32 |
| 34 | `DropGold` | Amount:UInt32 |
| 35 | `PickUp` | - |
| 36 | `RequestMapInfo` | MapIndex:Int32 |
| 37 | `RequestMonsterInfo` | MonsterIndex:Int32 |
| 38 | `RequestNPCInfo` | NPCIndex:Int32 |
| 39 | `RequestItemInfo` | ItemIndex:Int32 |
| 40 | `TeleportToNPC` | ObjectID:UInt32 |
| 41 | `SearchMap` | Text:string |
| 42 | `Inspect` | Hero:Boolean, ObjectID:UInt32, Ranking:Boolean |
| 43 | `Observe` | Name:string |
| 44 | `ChangeAMode` | Mode:AttackMode |
| 45 | `ChangePMode` | Mode:PetMode |
| 46 | `ChangeTrade` | AllowTrade:Boolean |
| 47 | `Attack` | Direction:MirDirection, Spell:Spell |
| 48 | `RangeAttack` | Direction:MirDirection, Location:Point, TargetID:UInt32, TargetLocation:Point |
| 49 | `Harvest` | Direction:MirDirection |
| 50 | `CallNPC` | Key:string, ObjectID:UInt32 |
| 51 | `BuyItem` | Count:UInt16, ItemIndex:UInt64, Type:PanelType |
| 52 | `SellItem` | Count:UInt16, UniqueID:UInt64 |
| 53 | `CraftItem` | Count:UInt16, Slots:Int32[], UniqueID:UInt64 |
| 54 | `RepairItem` | UniqueID:UInt64 |
| 55 | `BuyItemBack` | Count:UInt16, UniqueID:UInt64 |
| 56 | `SRepairItem` | UniqueID:UInt64 |
| 57 | `MagicKey` | Key:Byte, OldKey:Byte, Spell:Spell |
| 58 | `Magic` | Direction:MirDirection, Location:Point, ObjectID:UInt32, Spell:Spell, SpellTargetLock:Boolean, TargetID:UInt32 |
| 59 | `SwitchGroup` | AllowGroup:Boolean |
| 60 | `AddMember` | Name:string |
| 61 | `DelMember` | Name:string |
| 62 | `GroupInvite` | AcceptInvite:Boolean |
| 63 | `NewHero` | Class:MirClass, Gender:MirGender, Name:string |
| 64 | `SetAutoPotValue` | Stat:Stat, Value:UInt32 |
| 65 | `SetAutoPotItem` | Grid:MirGridType, ItemIndex:Int32 |
| 66 | `SetHeroBehaviour` | Behaviour:HeroBehaviour |
| 67 | `ChangeHero` | ListIndex:Int32 |
| 68 | `TownRevive` | - |
| 69 | `SpellToggle` | CanUse:Boolean, Spell:Spell, canUse:SpellToggleState |
| 70 | `ConsignItem` | Price:UInt32, Type:MarketPanelType, UniqueID:UInt64 |
| 71 | `MarketSearch` | MarketType:MarketPanelType, Match:string, MaxShape:Int16, MinShape:Int16, Type:ItemType, Usermode:Boolean |
| 72 | `MarketRefresh` | - |
| 73 | `MarketPage` | Page:Int32 |
| 74 | `MarketBuy` | AuctionID:UInt64, BidPrice:UInt32 |
| 75 | `MarketGetBack` | AuctionID:UInt64, Mode:MarketCollectionMode |
| 76 | `MarketSellNow` | AuctionID:UInt64 |
| 77 | `RequestUserName` | UserID:UInt32 |
| 78 | `RequestChatItem` | ChatItemID:UInt64 |
| 79 | `EditGuildMember` | ChangeType:Byte, Name:string, RankIndex:Byte, RankName:string |
| 80 | `EditGuildNotice` | notice:List<string> |
| 81 | `GuildInvite` | AcceptInvite:Boolean |
| 82 | `GuildNameReturn` | Name:string |
| 83 | `RequestGuildInfo` | Type:Byte |
| 84 | `GuildStorageGoldChange` | Amount:UInt32, Type:Byte |
| 85 | `GuildStorageItemChange` | From:Int32, To:Int32, Type:Byte |
| 86 | `GuildWarReturn` | Name:string |
| 87 | `MarriageRequest` | - |
| 88 | `MarriageReply` | AcceptInvite:Boolean |
| 89 | `ChangeMarriage` | - |
| 90 | `DivorceRequest` | - |
| 91 | `DivorceReply` | AcceptInvite:Boolean |
| 92 | `AddMentor` | Name:string |
| 93 | `MentorReply` | AcceptInvite:Boolean |
| 94 | `AllowMentor` | - |
| 95 | `CancelMentor` | - |
| 96 | `TradeRequest` | - |
| 97 | `TradeReply` | AcceptInvite:Boolean |
| 98 | `TradeGold` | Amount:UInt32 |
| 99 | `TradeConfirm` | Locked:Boolean |
| 100 | `TradeCancel` | - |
| 101 | `EquipSlotItem` | Grid:MirGridType, GridTo:MirGridType, To:Int32, ToUniqueID:UInt64, UniqueID:UInt64 |
| 102 | `FishingCast` | CastOut:Boolean |
| 103 | `FishingChangeAutocast` | AutoCast:Boolean |
| 104 | `AcceptQuest` | NPCIndex:UInt32, QuestIndex:Int32 |
| 105 | `FinishQuest` | QuestIndex:Int32, SelectedItemIndex:Int32 |
| 106 | `AbandonQuest` | QuestIndex:Int32 |
| 107 | `ShareQuest` | QuestIndex:Int32 |
| 108 | `AcceptReincarnation` | - |
| 109 | `CancelReincarnation` | - |
| 110 | `CombineItem` | Grid:MirGridType, IDFrom:UInt64, IDTo:UInt64 |
| 111 | `AwakeningNeedMaterials` | Type:AwakeType, UniqueID:UInt64 |
| 112 | `AwakeningLockedItem` | Locked:Boolean, UniqueID:UInt64 |
| 113 | `Awakening` | PositionIdx:UInt32, Type:AwakeType, UniqueID:UInt64 |
| 114 | `DisassembleItem` | UniqueID:UInt64 |
| 115 | `DowngradeAwakening` | UniqueID:UInt64 |
| 116 | `ResetAddedItem` | UniqueID:UInt64 |
| 117 | `SendMail` | Gold:UInt32, ItemsIdx:UInt64[], Message:string, Name:string, Stamped:Boolean |
| 118 | `ReadMail` | MailID:UInt64 |
| 119 | `CollectParcel` | MailID:UInt64 |
| 120 | `DeleteMail` | MailID:UInt64 |
| 121 | `LockMail` | Lock:Boolean, MailID:UInt64 |
| 122 | `MailLockedItem` | Locked:Boolean, UniqueID:UInt64 |
| 123 | `MailCost` | Gold:UInt32, ItemsIdx:UInt64[], Stamped:Boolean |
| 124 | `UpdateIntelligentCreature` | Creature:ClientIntelligentCreature, ReleaseMe:Boolean, SummonMe:Boolean, UnSummonMe:Boolean |
| 125 | `IntelligentCreaturePickup` | Location:Point, MouseMode:Boolean |
| 126 | `RequestIntelligentCreatureUpdates` | Update:Boolean |
| 127 | `AddFriend` | Blocked:Boolean, Name:string |
| 128 | `RemoveFriend` | CharacterIndex:Int32 |
| 129 | `RefreshFriends` | - |
| 130 | `AddMemo` | CharacterIndex:Int32, Memo:string |
| 131 | `GuildBuffUpdate` | Action:Byte, Id:Int32 |
| 132 | `NPCConfirmInput` | NPCID:UInt32, PageName:string, Value:string |
| 133 | `GameshopBuy` | GIndex:Int32, PType:Int32, Quantity:Byte |
| 134 | `ReportIssue` | Image:Byte[], ImageChunk:Int32, ImageSize:Int32, Message:string |
| 135 | `GetRanking` | OnlineOnly:Boolean, RankIndex:Int32, RankType:Byte |
| 136 | `Opendoor` | DoorIndex:Byte |
| 137 | `GetRentedItems` | - |
| 138 | `ItemRentalRequest` | - |
| 139 | `ItemRentalFee` | Amount:UInt32 |
| 140 | `ItemRentalPeriod` | Days:UInt32 |
| 141 | `DepositRentalItem` | From:Int32, To:Int32 |
| 142 | `RetrieveRentalItem` | From:Int32, To:Int32 |
| 143 | `CancelItemRental` | - |
| 144 | `ItemRentalLockFee` | - |
| 145 | `ItemRentalLockItem` | - |
| 146 | `ConfirmItemRental` | - |
| 147 | `GuildTerritoryPage` | Page:Int32 |
| 148 | `PurchaseGuildTerritory` | Owner:string |
| 149 | `DeleteItem` | Count:UInt16, HeroInventory:Boolean, UniqueID:UInt64 |
| 150 | `UnlockStorage` | Password:string |
| 151 | `SetStoragePassword` | CurrentPassword:string, NewPassword:string |
| 152 | `RemoveStoragePassword` | CurrentPassword:string |

## 服务端 -> 客户端

| Index | Packet | 字段 |
| --- | --- | --- |
| 0 | `Connected` | - |
| 1 | `ClientVersion` | Result:Byte |
| 2 | `Disconnect` | Reason:Byte |
| 3 | `KeepAlive` | Time:Int64 |
| 4 | `NewAccount` | Result:Byte |
| 5 | `ChangePassword` | Result:Byte |
| 6 | `ChangePasswordBanned` | ExpiryDate:DateTime, Reason:string |
| 7 | `Login` | Result:Byte |
| 8 | `LoginBanned` | ExpiryDate:DateTime, Reason:string |
| 9 | `LoginSuccess` | Characters:List<SelectInfo> |
| 10 | `NewCharacter` | Result:Byte |
| 11 | `NewCharacterSuccess` | CharInfo:SelectInfo |
| 12 | `DeleteCharacter` | Result:Byte |
| 13 | `DeleteCharacterSuccess` | CharacterIndex:Int32 |
| 14 | `StartGame` | Resolution:Int32, Result:Byte |
| 15 | `StartGameBanned` | ExpiryDate:DateTime, Reason:string |
| 16 | `StartGameDelay` | Milliseconds:Int64 |
| 17 | `MapInformation` | BigMap:UInt16, FileName:string, Fire:Boolean, Lightning:Boolean, Lights:LightSetting, MapDarkLight:Byte, MapIndex:Int32, MiniMap:UInt16, Music:UInt16, Title:string, WeatherParticles:WeatherSetting |
| 18 | `NewMapInfo` | Info:ClientMapInfo, MapIndex:Int32 |
| 19 | `WorldMapSetupInfo` | Setup:WorldMapSetup, TeleportToNPCCost:Int32 |
| 20 | `SearchMapResult` | MapIndex:Int32, NPCIndex:UInt32 |
| 21 | `UserInformation` | AllowObserve:Boolean, Class:MirClass, CreatureSummoned:Boolean, Credit:UInt32, Direction:MirDirection, Equipment:UserItem[], ExpandedStorageExpiryTime:DateTime, Experience:Int64, Gender:MirGender, Gold:UInt32, GuildName:string, GuildRank:string, HP:Int32, Hair:Byte, HasExpandedStorage:Boolean, HasHero:Boolean, HasStoragePassword:Boolean, HeroBehaviour:HeroBehaviour, IntelligentCreatures:List<ClientIntelligentCreature>, Inventory:UserItem[], Level:UInt16, LevelEffects:LevelEffects, Location:Point, MP:Int32, Magics:List<ClientMagic>, MaxExperience:Int64, Name:string, NameColour:Color, ObjectID:UInt32, Observer:Boolean, QuestInventory:UserItem[], RealId:UInt32, RequireStoragePassword:Boolean, StoragePasswordLastSet:DateTime, SummonedCreatureType:IntelligentCreatureType |
| 22 | `UserSlotsRefresh` | Equipment:UserItem[], Inventory:UserItem[] |
| 23 | `UserLocation` | Direction:MirDirection, Location:Point |
| 24 | `ObjectPlayer` | Armour:Int16, Buffs:List<BuffType>, Class:MirClass, Dead:Boolean, Direction:MirDirection, Effect:SpellEffect, ElementOrbEffect:UInt32, ElementOrbLvl:UInt32, ElementOrbMax:UInt32, Extra:Boolean, Fishing:Boolean, Gender:MirGender, GuildName:string, GuildRankName:string, Hair:Byte, Hidden:Boolean, Level:UInt16, LevelEffects:LevelEffects, Light:Byte, Location:Point, MountType:Int16, Name:string, NameColour:Color, ObjectID:UInt32, Poison:PoisonType, RidingMount:Boolean, TransformType:Int16, Weapon:Int16, WeaponEffect:Int16, WingEffect:Byte |
| 25 | `ObjectHero` | OwnerName:string |
| 26 | `ObjectRemove` | ObjectID:UInt32 |
| 27 | `ObjectTurn` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 28 | `ObjectWalk` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 29 | `ObjectRun` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 30 | `Chat` | Message:string, Type:ChatType |
| 31 | `ObjectChat` | ObjectID:UInt32, Text:string, Type:ChatType |
| 32 | `NewItemInfo` | Info:ItemInfo |
| 33 | `NewMonsterInfo` | Info:ClientMonsterInfo |
| 34 | `NewNPCInfo` | Info:ClientNPCInfo |
| 35 | `NewHeroInfo` | Info:ClientHeroInformation, StorageIndex:Int32 |
| 36 | `NewChatItem` | Item:UserItem |
| 37 | `MoveItem` | From:Int32, Grid:MirGridType, Success:Boolean, To:Int32 |
| 38 | `EquipItem` | Grid:MirGridType, Success:Boolean, To:Int32, UniqueID:UInt64 |
| 39 | `MergeItem` | GridFrom:MirGridType, GridTo:MirGridType, IDFrom:UInt64, IDTo:UInt64, Success:Boolean |
| 40 | `RemoveItem` | Grid:MirGridType, Success:Boolean, To:Int32, UniqueID:UInt64 |
| 41 | `RemoveSlotItem` | Grid:MirGridType, GridTo:MirGridType, Success:Boolean, To:Int32, UniqueID:UInt64 |
| 42 | `TakeBackItem` | From:Int32, Success:Boolean, To:Int32 |
| 43 | `StoreItem` | From:Int32, Success:Boolean, To:Int32 |
| 44 | `SplitItem` | Grid:MirGridType, Item:UserItem |
| 45 | `SplitItem1` | Count:UInt16, Grid:MirGridType, Success:Boolean, UniqueID:UInt64 |
| 46 | `DepositRefineItem` | From:Int32, Success:Boolean, To:Int32 |
| 47 | `RetrieveRefineItem` | From:Int32, Success:Boolean, To:Int32 |
| 48 | `RefineCancel` | Unlock:Boolean |
| 49 | `RefineItem` | UniqueID:UInt64 |
| 50 | `DepositTradeItem` | From:Int32, Success:Boolean, To:Int32 |
| 51 | `RetrieveTradeItem` | From:Int32, Success:Boolean, To:Int32 |
| 52 | `UseItem` | Grid:MirGridType, Success:Boolean, UniqueID:UInt64 |
| 53 | `DropItem` | Count:UInt16, HeroItem:Boolean, Success:Boolean, UniqueID:UInt64 |
| 54 | `TakeBackHeroItem` | From:Int32, Success:Boolean, To:Int32 |
| 55 | `TransferHeroItem` | From:Int32, Success:Boolean, To:Int32 |
| 56 | `PlayerUpdate` | Armour:Int16, Light:Byte, ObjectID:UInt32, Weapon:Int16, WeaponEffect:Int16, WingEffect:Byte |
| 57 | `PlayerInspect` | AllowObserve:Boolean, Class:MirClass, Equipment:UserItem[], Gender:MirGender, GuildName:string, GuildRank:string, Hair:Byte, IsHero:Boolean, Level:UInt16, LoverName:string, Name:string |
| 58 | `LogOutSuccess` | Characters:List<SelectInfo> |
| 59 | `LogOutFailed` | - |
| 60 | `ReturnToLogin` | - |
| 61 | `TimeOfDay` | Lights:LightSetting |
| 62 | `ChangeAMode` | Mode:AttackMode |
| 63 | `ChangePMode` | Mode:PetMode |
| 64 | `ObjectItem` | Image:UInt16, Location:Point, Name:string, NameColour:Color, ObjectID:UInt32, grade:ItemGrade |
| 65 | `ObjectGold` | Gold:UInt32, Location:Point, ObjectID:UInt32 |
| 66 | `GainedItem` | Item:UserItem |
| 67 | `GainedGold` | Gold:UInt32 |
| 68 | `LoseGold` | Gold:UInt32 |
| 69 | `GainedCredit` | Credit:UInt32 |
| 70 | `LoseCredit` | Credit:UInt32 |
| 71 | `ObjectMonster` | AI:Byte, BindingShotCenter:Boolean, Buffs:List<BuffType>, Dead:Boolean, Direction:MirDirection, Effect:Byte, Extra:Boolean, ExtraByte:Byte, Hidden:Boolean, Image:Monster, Light:Byte, Location:Point, MasterObjectId:UInt32, Name:string, NameColour:Color, ObjectID:UInt32, Poison:PoisonType, Rarity:MonsterType, ShockTime:Int64, Skeleton:Boolean |
| 72 | `ObjectAttack` | Direction:MirDirection, Level:Byte, Location:Point, ObjectID:UInt32, Spell:Spell, Type:Byte |
| 73 | `Struck` | AttackerID:UInt32 |
| 74 | `ObjectStruck` | AttackerID:UInt32, Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 75 | `DamageIndicator` | Damage:Int32, ObjectID:UInt32, Type:DamageType |
| 76 | `DuraChanged` | CurrentDura:UInt16, UniqueID:UInt64 |
| 77 | `HealthChanged` | HP:Int32, MP:Int32 |
| 78 | `HeroHealthChanged` | HP:Int32, MP:Int32 |
| 79 | `DeleteItem` | Count:UInt16, UniqueID:UInt64 |
| 80 | `Death` | Direction:MirDirection, Location:Point |
| 81 | `ObjectDied` | Direction:MirDirection, Location:Point, ObjectID:UInt32, Type:Byte |
| 82 | `ColourChanged` | NameColour:Color |
| 83 | `ObjectColourChanged` | NameColour:Color, ObjectID:UInt32 |
| 84 | `ObjectGuildNameChanged` | GuildName:string, ObjectID:UInt32 |
| 85 | `GainExperience` | Amount:UInt32 |
| 86 | `GainHeroExperience` | Amount:UInt32 |
| 87 | `LevelChanged` | Experience:Int64, Level:UInt16, MaxExperience:Int64 |
| 88 | `HeroLevelChanged` | Experience:Int64, Level:UInt16, MaxExperience:Int64 |
| 89 | `ObjectLeveled` | ObjectID:UInt32 |
| 90 | `ObjectHarvest` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 91 | `ObjectHarvested` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 92 | `ObjectNPC` | Colour:Color, Direction:MirDirection, Image:UInt16, Location:Point, Name:string, NameColour:Color, ObjectID:UInt32, QuestIDs:List<Int32> |
| 93 | `NPCResponse` | Page:List<string> |
| 94 | `ObjectHide` | ObjectID:UInt32 |
| 95 | `ObjectShow` | ObjectID:UInt32 |
| 96 | `Poisoned` | Poison:PoisonType |
| 97 | `ObjectPoisoned` | ObjectID:UInt32, Poison:PoisonType |
| 98 | `MapChanged` | BigMap:UInt16, Direction:MirDirection, FileName:string, Lights:LightSetting, Location:Point, MapDarkLight:Byte, MapIndex:Int32, MiniMap:UInt16, Music:UInt16, Title:string, Weather:WeatherSetting |
| 99 | `ObjectTeleportOut` | ObjectID:UInt32, Type:Byte |
| 100 | `ObjectTeleportIn` | ObjectID:UInt32, Type:Byte |
| 101 | `TeleportIn` | - |
| 102 | `NPCGoods` | HideAddedStats:Boolean, List:List<UserItem>, Rate:Single, Type:PanelType |
| 103 | `NPCSell` | - |
| 104 | `NPCRepair` | Rate:Single |
| 105 | `NPCSRepair` | Rate:Single |
| 106 | `NPCRefine` | Rate:Single, Refining:Boolean |
| 107 | `NPCCheckRefine` | - |
| 108 | `NPCCollectRefine` | Success:Boolean |
| 109 | `NPCReplaceWedRing` | Rate:Single |
| 110 | `NPCStorage` | - |
| 111 | `SellItem` | Count:UInt16, Success:Boolean, UniqueID:UInt64 |
| 112 | `CraftItem` | Success:Boolean |
| 113 | `RepairItem` | UniqueID:UInt64 |
| 114 | `ItemRepaired` | CurrentDura:UInt16, MaxDura:UInt16, UniqueID:UInt64 |
| 115 | `ItemSlotSizeChanged` | SlotSize:Int32, UniqueID:UInt64 |
| 116 | `ItemSealChanged` | ExpiryDate:DateTime, UniqueID:UInt64 |
| 117 | `NewMagic` | Hero:Boolean, Magic:ClientMagic |
| 118 | `RemoveMagic` | PlaceId:Int32 |
| 119 | `MagicLeveled` | Experience:UInt16, Level:Byte, ObjectID:UInt32, Spell:Spell |
| 120 | `Magic` | Cast:Boolean, Level:Byte, SecondaryTargetIDs:List<UInt32>, Spell:Spell, Target:Point, TargetID:UInt32 |
| 121 | `MagicDelay` | Delay:Int64, ObjectID:UInt32, Spell:Spell |
| 122 | `MagicCast` | Spell:Spell |
| 123 | `ObjectMagic` | Cast:Boolean, Direction:MirDirection, Level:Byte, Location:Point, ObjectID:UInt32, SecondaryTargetIDs:List<UInt32>, SelfBroadcast:Boolean, Spell:Spell, Target:Point, TargetID:UInt32 |
| 124 | `ObjectEffect` | DelayTime:UInt32, Effect:SpellEffect, EffectType:UInt32, ObjectID:UInt32, Time:UInt32 |
| 125 | `ObjectProjectile` | Destination:UInt32, Source:UInt32, Spell:Spell |
| 126 | `RangeAttack` | Spell:Spell, Target:Point, TargetID:UInt32 |
| 127 | `Pushed` | Direction:MirDirection, Location:Point |
| 128 | `ObjectPushed` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 129 | `ObjectName` | Name:string, ObjectID:UInt32 |
| 130 | `UserStorage` | Storage:UserItem[] |
| 131 | `SwitchGroup` | AllowGroup:Boolean |
| 132 | `DeleteGroup` | - |
| 133 | `DeleteMember` | Name:string |
| 134 | `GroupInvite` | Name:string |
| 135 | `AddMember` | Name:string |
| 136 | `Revived` | - |
| 137 | `ObjectRevived` | Effect:Boolean, ObjectID:UInt32 |
| 138 | `SpellToggle` | CanUse:Boolean, ObjectID:UInt32, Spell:Spell |
| 139 | `ObjectHealth` | Expire:Byte, ObjectID:UInt32, Percent:Byte |
| 140 | `ObjectMana` | ObjectID:UInt32, Percent:Byte |
| 141 | `MapEffect` | Effect:SpellEffect, Location:Point, Value:Byte |
| 142 | `AllowObserve` | Allow:Boolean |
| 143 | `ObjectRangeAttack` | Direction:MirDirection, Level:Byte, Location:Point, ObjectID:UInt32, Spell:Spell, Target:Point, TargetID:UInt32, Type:Byte |
| 144 | `AddBuff` | Buff:ClientBuff |
| 145 | `RemoveBuff` | ObjectID:UInt32, Type:BuffType |
| 146 | `PauseBuff` | ObjectID:UInt32, Paused:Boolean, Type:BuffType |
| 147 | `ObjectHidden` | Hidden:Boolean, ObjectID:UInt32 |
| 148 | `RefreshItem` | Item:UserItem |
| 149 | `ObjectSpell` | Direction:MirDirection, Location:Point, ObjectID:UInt32, Param:Boolean, Spell:Spell |
| 150 | `UserDash` | Direction:MirDirection, Location:Point |
| 151 | `ObjectDash` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 152 | `UserDashFail` | Direction:MirDirection, Location:Point |
| 153 | `ObjectDashFail` | Direction:MirDirection, Location:Point, ObjectID:UInt32 |
| 154 | `NPCConsign` | - |
| 155 | `NPCMarket` | Listings:List<ClientAuction>, Pages:Int32, UserMode:Boolean |
| 156 | `NPCMarketPage` | Listings:List<ClientAuction> |
| 157 | `ConsignItem` | Success:Boolean, UniqueID:UInt64 |
| 158 | `MarketFail` | Reason:Byte |
| 159 | `MarketSuccess` | Message:string |
| 160 | `ObjectSitDown` | Direction:MirDirection, Location:Point, ObjectID:UInt32, Sitting:Boolean |
| 161 | `InTrapRock` | Trapped:Boolean |
| 162 | `BaseStatsInfo` | Stats:BaseStats |
| 163 | `HeroBaseStatsInfo` | Stats:BaseStats |
| 164 | `UserName` | Id:UInt32, Name:string |
| 165 | `ChatItemStats` | ChatItemId:UInt64, Stats:UserItem |
| 166 | `GuildNoticeChange` | notice:List<string>, update:Int32 |
| 167 | `GuildMemberChange` | Name:string, RankIndex:Byte, Ranks:List<GuildRank>, Status:Byte |
| 168 | `GuildStatus` | BuffCount:Byte, Experience:Int64, Gold:UInt32, GuildName:string, GuildRankName:string, ItemCount:Byte, Level:Byte, MaxExperience:Int64, MaxMembers:Int32, MemberCount:Int32, MyOptions:GuildRankOptions, MyRankId:Int32, SparePoints:Byte, Voting:Boolean |
| 169 | `GuildInvite` | Name:string |
| 170 | `GuildExpGain` | Amount:UInt32 |
| 171 | `GuildNameRequest` | - |
| 172 | `GuildStorageGoldChange` | Amount:UInt32, Name:string, Type:Byte |
| 173 | `GuildStorageItemChange` | From:Int32, Item:GuildStorageItem, To:Int32, Type:Byte, User:Int32 |
| 174 | `GuildStorageList` | Items:GuildStorageItem[] |
| 175 | `GuildRequestWar` | - |
| 176 | `HeroCreateRequest` | CanCreateClass:Boolean[] |
| 177 | `NewHero` | Result:Byte |
| 178 | `HeroInformation` | AutoHPPercent:Byte, AutoMPPercent:Byte, AutoPot:Boolean, HPItemIndex:Int32, MPItemIndex:Int32 |
| 179 | `UpdateHeroSpawnState` | State:HeroSpawnState |
| 180 | `UnlockHeroAutoPot` | - |
| 181 | `SetAutoPotValue` | Stat:Stat, Value:UInt32 |
| 182 | `SetAutoPotItem` | Grid:MirGridType, ItemIndex:Int32 |
| 183 | `SetHeroBehaviour` | Behaviour:HeroBehaviour |
| 184 | `ManageHeroes` | CurrentHero:ClientHeroInformation, Heroes:ClientHeroInformation[], MaximumCount:Int32 |
| 185 | `ChangeHero` | FromIndex:Int32 |
| 186 | `DefaultNPC` | ObjectID:UInt32 |
| 187 | `NPCUpdate` | NPCID:UInt32 |
| 188 | `NPCImageUpdate` | Colour:Color, Image:UInt16, ObjectID:UInt32 |
| 189 | `MarriageRequest` | Name:string |
| 190 | `DivorceRequest` | Name:string |
| 191 | `MentorRequest` | Level:UInt16, Name:string |
| 192 | `TradeRequest` | Name:string |
| 193 | `TradeAccept` | Name:string |
| 194 | `TradeGold` | Amount:UInt32 |
| 195 | `TradeItem` | TradeItems:UserItem[] |
| 196 | `TradeConfirm` | - |
| 197 | `TradeCancel` | Unlock:Boolean |
| 198 | `MountUpdate` | MountType:Int16, ObjectID:UInt32, RidingMount:Boolean |
| 199 | `EquipSlotItem` | Grid:MirGridType, GridTo:MirGridType, Success:Boolean, To:Int32, UniqueID:UInt64 |
| 200 | `FishingUpdate` | ChancePercent:Int32, Fishing:Boolean, FishingPoint:Point, FoundFish:Boolean, ObjectID:UInt32, ProgressPercent:Int32 |
| 201 | `ChangeQuest` | Quest:ClientQuestProgress, QuestState:QuestState, TrackQuest:Boolean |
| 202 | `CompleteQuest` | CompletedQuests:List<Int32> |
| 203 | `ShareQuest` | QuestIndex:Int32, SharerName:string |
| 204 | `NewQuestInfo` | Info:ClientQuestInfo |
| 205 | `GainedQuestItem` | Item:UserItem |
| 206 | `DeleteQuestItem` | Count:UInt16, UniqueID:UInt64 |
| 207 | `CancelReincarnation` | - |
| 208 | `RequestReincarnation` | - |
| 209 | `UserBackStep` | Direction:MirDirection, Location:Point |
| 210 | `ObjectBackStep` | Direction:MirDirection, Distance:Int32, Location:Point, ObjectID:UInt32 |
| 211 | `UserDashAttack` | Direction:MirDirection, Location:Point |
| 212 | `ObjectDashAttack` | Direction:MirDirection, Distance:Int32, Location:Point, ObjectID:UInt32 |
| 213 | `UserAttackMove` | Direction:MirDirection, Location:Point |
| 214 | `CombineItem` | Destroy:Boolean, Grid:MirGridType, IDFrom:UInt64, IDTo:UInt64, Success:Boolean |
| 215 | `ItemUpgraded` | Item:UserItem |
| 216 | `SetConcentration` | Enabled:Boolean, Interrupted:Boolean, ObjectID:UInt32 |
| 217 | `SetElemental` | Casted:Boolean, ElementType:UInt32, Enabled:Boolean, ExpLast:UInt32, ObjectID:UInt32, Value:UInt32 |
| 218 | `RemoveDelayedExplosion` | ObjectID:UInt32 |
| 219 | `ObjectDeco` | Image:Int32, Location:Point, ObjectID:UInt32 |
| 220 | `ObjectSneaking` | ObjectID:UInt32, SneakingActive:Boolean |
| 221 | `ObjectLevelEffects` | LevelEffects:LevelEffects, ObjectID:UInt32 |
| 222 | `SetBindingShot` | Enabled:Boolean, ObjectID:UInt32, Value:Int64 |
| 223 | `SendOutputMessage` | Message:string, Type:OutputMessageType |
| 224 | `NPCAwakening` | - |
| 225 | `NPCDisassemble` | - |
| 226 | `NPCDowngrade` | - |
| 227 | `NPCReset` | - |
| 228 | `AwakeningNeedMaterials` | Materials:ItemInfo[], MaterialsCount:Byte[] |
| 229 | `AwakeningLockedItem` | Locked:Boolean, UniqueID:UInt64 |
| 230 | `Awakening` | removeID:Int64, result:Int32 |
| 231 | `ReceiveMail` | Mail:List<ClientMail> |
| 232 | `MailLockedItem` | Locked:Boolean, UniqueID:UInt64 |
| 233 | `MailSendRequest` | - |
| 234 | `MailSent` | Result:SByte |
| 235 | `ParcelCollected` | Result:SByte |
| 236 | `MailCost` | Cost:UInt32 |
| 237 | `ResizeInventory` | Size:Int32 |
| 238 | `ResizeStorage` | ExpiryTime:DateTime, HasExpandedStorage:Boolean, Size:Int32 |
| 239 | `NewIntelligentCreature` | Creature:ClientIntelligentCreature |
| 240 | `UpdateIntelligentCreatureList` | CreatureList:List<ClientIntelligentCreature>, CreatureSummoned:Boolean, PearlCount:Int32, SummonedCreatureType:IntelligentCreatureType |
| 241 | `IntelligentCreatureEnableRename` | - |
| 242 | `IntelligentCreaturePickup` | ObjectID:UInt32 |
| 243 | `NPCPearlGoods` | List:List<UserItem>, Rate:Single, Type:PanelType |
| 244 | `TransformUpdate` | ObjectID:UInt32, TransformType:Int16 |
| 245 | `FriendUpdate` | Friends:List<ClientFriend> |
| 246 | `LoverUpdate` | Date:DateTime, MapName:string, MarriedDays:Int16, Name:string |
| 247 | `MentorUpdate` | Level:UInt16, MenteeEXP:Int64, Name:string, Online:Boolean |
| 248 | `GuildBuffList` | ActiveBuffs:List<GuildBuff>, GuildBuffs:List<GuildBuffInfo>, Remove:Byte |
| 249 | `NPCRequestInput` | NPCID:UInt32, PageName:string |
| 250 | `GameShopInfo` | Item:GameShopItem, StockLevel:Int32 |
| 251 | `GameShopStock` | GIndex:Int32, StockLevel:Int32 |
| 252 | `Rankings` | Count:Int32, ListingDetails:List<RankCharacterInfo>, Listings:List<Int64>, MyRank:Int32, RankType:Byte |
| 253 | `Opendoor` | Close:Boolean, DoorIndex:Byte |
| 254 | `GetRentedItems` | RentedItems:List<ItemRentalInformation> |
| 255 | `ItemRentalRequest` | Name:string, Renting:Boolean |
| 256 | `ItemRentalFee` | Amount:UInt32 |
| 257 | `ItemRentalPeriod` | Days:UInt32 |
| 258 | `DepositRentalItem` | From:Int32, Success:Boolean, To:Int32 |
| 259 | `RetrieveRentalItem` | From:Int32, Success:Boolean, To:Int32 |
| 260 | `UpdateRentalItem` | HasData:Boolean, LoanItem:UserItem |
| 261 | `CancelItemRental` | - |
| 262 | `ItemRentalLock` | GoldLocked:Boolean, ItemLocked:Boolean, Success:Boolean |
| 263 | `ItemRentalPartnerLock` | GoldLocked:Boolean, ItemLocked:Boolean |
| 264 | `CanConfirmItemRental` | - |
| 265 | `ConfirmItemRental` | - |
| 266 | `NewRecipeInfo` | Info:ClientRecipeInfo |
| 267 | `OpenBrowser` | Url:string |
| 268 | `PlaySound` | Sound:Int32 |
| 269 | `SetTimer` | Key:string, Seconds:Int32, Type:Byte |
| 270 | `ExpireTimer` | Key:string |
| 271 | `UpdateNotice` | Notice:Notice |
| 272 | `Roll` | AutoRoll:Boolean, Page:string, Result:Int32, Type:Int32 |
| 273 | `SetCompass` | Location:Point |
| 274 | `GroupMembersMap` | PlayerMap:string, PlayerName:string |
| 275 | `SendMemberLocation` | MemberLocation:Point, MemberName:string |
| 276 | `GuildTerritoryPage` | Listings:List<ClientGTMap>, length:Int32 |
| 277 | `StorageUnlockResult` | HasPassword:Boolean, Result:Byte |
| 278 | `StoragePasswordResult` | HasPassword:Boolean, LastSetTime:DateTime, Removing:Boolean, Result:Byte |
