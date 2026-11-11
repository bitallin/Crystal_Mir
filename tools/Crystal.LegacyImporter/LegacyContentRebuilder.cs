using System.Runtime.CompilerServices;
using System.Text.Json;
using Crystal.Content;
using Server;
using Server.Library.MirDatabase;
using Server.MirDatabase;
using Server.MirEnvir;
using Shared;

namespace Crystal.LegacyImporter;

public sealed class LegacyRebuildEnvironment : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _originalDirectory;
    private bool _disposed;

    private LegacyRebuildEnvironment(string temporaryDirectory, string originalDirectory)
    {
        _temporaryDirectory = temporaryDirectory;
        _originalDirectory = originalDirectory;
    }

    public static LegacyRebuildEnvironment Open()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"crystal-legacy-rebuild-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            Packet.IsServer = true;
            Settings.RandomItemStatsList.Clear();
            Settings.Load();
            Envir.LoadVersion = Envir.Version;
            Envir.LoadCustomVersion = Envir.CustomVersion;
            return new LegacyRebuildEnvironment(temporaryDirectory, originalDirectory);
        }
        catch
        {
            Environment.CurrentDirectory = originalDirectory;
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Environment.CurrentDirectory = _originalDirectory;
        try { Directory.Delete(_temporaryDirectory, recursive: true); } catch { }
    }
}

public static class LegacyContentRebuilder
{
    public static Envir Build(ContentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var issues = ContentSnapshotValidator.Validate(snapshot);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                "Snapshot validation failed: " + string.Join("; ", issues.Select(issue => issue.Message)));
        }

        var ordered = ContentSnapshotWriter.ValidateAndOrder(snapshot.Entities);
        var entityRoots = new Dictionary<string, List<ContentEntity>>(StringComparer.Ordinal);
        var references = new Dictionary<(string EntityType, long Index), object>();

        foreach (var entity in ordered)
        {
            if (!entityRoots.TryGetValue(entity.EntityType, out var roots))
            {
                roots = new List<ContentEntity>();
                entityRoots[entity.EntityType] = roots;
            }

            roots.Add(entity);
            var type = GetEntityType(entity.EntityType);
            if (type is null) continue;

            var instance = CreateInstance(type);
            if (!references.TryAdd((entity.EntityType, entity.LocalIndex), instance))
            {
                throw new InvalidDataException(
                    $"Duplicate entity reference {entity.EntityType}.{entity.LocalIndex}.");
            }
        }

        var deserializer = new LegacyJsonDeserializer(references);
        foreach (var (entityType, roots) in entityRoots)
        {
            var type = GetEntityType(entityType);
            if (type is null) continue;

            foreach (var entity in roots)
            {
                deserializer.DeserializeInto(
                    entity.Data,
                    references[(entity.EntityType, entity.LocalIndex)]);
            }
        }

        var envir = Envir.Edit;
        ResetEnvir(envir);

        envir.ItemInfoList.AddRange(GetRoots<ItemInfo>(entityRoots, ContentEntityTypes.Item, references));
        envir.MonsterInfoList.AddRange(GetRoots<MonsterInfo>(entityRoots, ContentEntityTypes.Monster, references));
        envir.MapInfoList.AddRange(GetRoots<MapInfo>(entityRoots, ContentEntityTypes.Map, references));
        envir.NPCInfoList.AddRange(GetRoots<NPCInfo>(entityRoots, ContentEntityTypes.Npc, references));
        envir.QuestInfoList.AddRange(GetRoots<QuestInfo>(entityRoots, ContentEntityTypes.Quest, references));
        envir.MagicInfoList.AddRange(GetRoots<MagicInfo>(entityRoots, ContentEntityTypes.Magic, references));
        envir.GameShopList.AddRange(GetRoots<GameShopItem>(entityRoots, ContentEntityTypes.GameShop, references));
        envir.ConquestInfoList.AddRange(GetRoots<ConquestInfo>(entityRoots, ContentEntityTypes.Conquest, references));

        var dragons = GetRoots<DragonInfo>(entityRoots, ContentEntityTypes.Dragon, references);
        if (dragons.Count != 1)
        {
            throw new InvalidDataException(
                $"Legacy database rebuild requires exactly one dragon entity, found {dragons.Count}.");
        }

        envir.DragonInfo = dragons[0];
        Settings.LinkGuildCreationItems(envir.ItemInfoList);
        ApplyLegacyMetadata(envir, snapshot.Manifest.Legacy, ordered);
        ValidateRespawnProjection(envir, ordered);
        return envir;
    }

    private static void ResetEnvir(Envir envir)
    {
        envir.MapIndex = 0;
        envir.ItemIndex = 0;
        envir.MonsterIndex = 0;
        envir.NPCIndex = 0;
        envir.QuestIndex = 0;
        envir.GameshopIndex = 0;
        envir.ConquestIndex = 0;
        envir.RespawnIndex = 0;
        envir.MapInfoList.Clear();
        envir.ItemInfoList.Clear();
        envir.MonsterInfoList.Clear();
        envir.MagicInfoList.Clear();
        envir.NPCInfoList.Clear();
        envir.QuestInfoList.Clear();
        envir.GameShopList.Clear();
        envir.ConquestInfoList.Clear();
        envir.GTMapList.Clear();
        envir.DragonInfo = new DragonInfo();
        envir.RespawnTick = new RespawnTimer();
    }

    private static List<T> GetRoots<T>(
        IReadOnlyDictionary<string, List<ContentEntity>> entityRoots,
        string entityType,
        IReadOnlyDictionary<(string EntityType, long Index), object> references)
    {
        if (!entityRoots.TryGetValue(entityType, out var roots)) return new List<T>();

        return roots.Select(entity =>
        {
            var value = references[(entity.EntityType, entity.LocalIndex)];
            return value is T typed
                ? typed
                : throw new InvalidDataException(
                    $"Entity {entity.ContentKey} resolved to {value.GetType().FullName}, expected {typeof(T).FullName}.");
        }).ToList();
    }

    private static void ApplyLegacyMetadata(
        Envir envir,
        JsonElement? legacy,
        IReadOnlyList<ContentEntity> ordered)
    {
        var metadata = LegacySnapshotMetadata.Read(legacy, ordered);
        envir.MapIndex = metadata.Indices.Map;
        envir.ItemIndex = metadata.Indices.Item;
        envir.MonsterIndex = metadata.Indices.Monster;
        envir.NPCIndex = metadata.Indices.Npc;
        envir.QuestIndex = metadata.Indices.Quest;
        envir.GameshopIndex = metadata.Indices.GameShop;
        envir.ConquestIndex = metadata.Indices.Conquest;
        envir.RespawnIndex = metadata.Indices.Respawn;

        envir.RespawnTick = new RespawnTimer
        {
            BaseSpawnRate = metadata.RespawnTick.BaseSpawnRate,
            CurrentTickcounter = metadata.RespawnTick.CurrentTickCounter,
            Respawn = metadata.RespawnTick.Options
                .Select(option => new RespawnTickOption
                {
                    UserCount = option.UserCount,
                    DelayLoss = option.DelayLoss
                })
                .ToList()
        };
        envir.GTMapList.AddRange(metadata.GtMaps);

        ApplyOrder(envir.MapInfoList, metadata.Order.Maps, map => map.Index, "map");
        ApplyOrder(envir.ItemInfoList, metadata.Order.Items, item => item.Index, "item");
        ApplyOrder(envir.MonsterInfoList, metadata.Order.Monsters, monster => monster.Index, "monster");
        ApplyOrder(envir.NPCInfoList, metadata.Order.Npcs, npc => npc.Index, "npc");
        ApplyOrder(envir.QuestInfoList, metadata.Order.Quests, quest => quest.Index, "quest");
        ApplyOrder(envir.MagicInfoList, metadata.Order.Magics, magic => (long)magic.Spell, "magic");
        ApplyOrder(envir.GameShopList, metadata.Order.GameShop, item => item.GIndex, "gameshop");
        ApplyOrder(envir.ConquestInfoList, metadata.Order.Conquests, conquest => conquest.Index, "conquest");
    }

    private static void ApplyOrder<T>(
        List<T> values,
        IReadOnlyList<long>? order,
        Func<T, long> keySelector,
        string entityType)
    {
        if (order is null) return;
        if (order.Count != values.Count)
        {
            throw new InvalidDataException(
                $"Legacy {entityType} order count mismatch: metadata={order.Count}, entities={values.Count}.");
        }

        var byKey = values.ToDictionary(keySelector);
        var ordered = new List<T>(order.Count);
        foreach (var key in order)
        {
            if (!byKey.TryGetValue(key, out var value))
            {
                throw new InvalidDataException(
                    $"Legacy {entityType} order references missing local index {key}.");
            }

            ordered.Add(value);
        }

        values.Clear();
        values.AddRange(ordered);
    }

    private static void ValidateRespawnProjection(Envir envir, IReadOnlyList<ContentEntity> ordered)
    {
        var respawnEntities = ordered
            .Where(entity => entity.EntityType == ContentEntityTypes.Respawn)
            .ToDictionary(entity => entity.LocalIndex);
        var expectedCount = envir.MapInfoList.Sum(map => map.Respawns.Count);
        if (respawnEntities.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"Respawn projection count mismatch: snapshot={respawnEntities.Count}, maps={expectedCount}.");
        }

        foreach (var map in envir.MapInfoList)
        {
            for (var ordinal = 0; ordinal < map.Respawns.Count; ordinal++)
            {
                var expected = LegacyContentMapper.CreateRespawnEntity(map, map.Respawns[ordinal], ordinal);
                if (!respawnEntities.TryGetValue(expected.LocalIndex, out var actual))
                {
                    throw new InvalidDataException(
                        $"Missing respawn projection {expected.ContentKey} for map {map.Index}.");
                }

                if (actual.ContentKey != expected.ContentKey ||
                    ContentJson.SerializeCanonical(actual.Data) != ContentJson.SerializeCanonical(expected.Data))
                {
                    throw new InvalidDataException(
                        $"Respawn projection mismatch for {expected.ContentKey}.");
                }
            }
        }
    }

    private static Type? GetEntityType(string entityType)
    {
        return entityType switch
        {
            ContentEntityTypes.Item => typeof(ItemInfo),
            ContentEntityTypes.Monster => typeof(MonsterInfo),
            ContentEntityTypes.Map => typeof(MapInfo),
            ContentEntityTypes.Npc => typeof(NPCInfo),
            ContentEntityTypes.Quest => typeof(QuestInfo),
            ContentEntityTypes.Magic => typeof(MagicInfo),
            ContentEntityTypes.GameShop => typeof(GameShopItem),
            ContentEntityTypes.Conquest => typeof(ConquestInfo),
            ContentEntityTypes.Dragon => typeof(DragonInfo),
            ContentEntityTypes.Respawn or ContentEntityTypes.Drop => null,
            ContentEntityTypes.Recipe or ContentEntityTypes.Buff or ContentEntityTypes.Script or ContentEntityTypes.Asset => throw new InvalidDataException(
                $"Entity type '{entityType}' is outside Server.MirDB and cannot be included in a legacy rebuild."),
            _ => throw new InvalidDataException($"Unsupported legacy entity type '{entityType}'.")
        };
    }

    private static object CreateInstance(Type type)
    {
        var constructor = type.GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        return constructor is not null
            ? constructor.Invoke(null)
            : RuntimeHelpers.GetUninitializedObject(type);
    }

    private sealed class LegacySnapshotMetadata
    {
        public required LegacyDatabaseIndices Indices { get; init; }

        public required RespawnTickSnapshot RespawnTick { get; init; }

        public required IReadOnlyList<GTMap> GtMaps { get; init; }

        public required LegacyEntityOrder Order { get; init; }

        public static LegacySnapshotMetadata Read(
            JsonElement? legacy,
            IReadOnlyList<ContentEntity> entities)
        {
            var document = legacy is { ValueKind: JsonValueKind.Object } value
                ? value
                : default;

            var indices = ReadIndices(document, entities);
            var respawnTick = ReadRespawnTick(document);
            var gtMaps = ReadGtMaps(document);
            var order = ReadOrder(document);
            return new LegacySnapshotMetadata
            {
                Indices = indices,
                RespawnTick = respawnTick,
                GtMaps = gtMaps,
                Order = order
            };
        }

        private static LegacyEntityOrder ReadOrder(JsonElement legacy)
        {
            if (legacy.ValueKind != JsonValueKind.Object ||
                !legacy.TryGetProperty("order", out var order) ||
                order.ValueKind != JsonValueKind.Object)
            {
                return LegacyEntityOrder.Empty;
            }

            return new LegacyEntityOrder
            {
                Maps = ReadInt64Array(order, "maps"),
                Items = ReadInt64Array(order, "items"),
                Monsters = ReadInt64Array(order, "monsters"),
                Npcs = ReadInt64Array(order, "npcs"),
                Quests = ReadInt64Array(order, "quests"),
                Magics = ReadInt64Array(order, "magics"),
                GameShop = ReadInt64Array(order, "gameshop"),
                Conquests = ReadInt64Array(order, "conquests")
            };
        }

        private static IReadOnlyList<long>? ReadInt64Array(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return array.EnumerateArray()
                .Select(item => item.TryGetInt64(out var value)
                    ? value
                    : throw new InvalidDataException(
                        $"Legacy order '{name}' contains a non-integer value."))
                .ToArray();
        }

        private static LegacyDatabaseIndices ReadIndices(
            JsonElement legacy,
            IReadOnlyList<ContentEntity> entities)
        {
            var map = MaxIndex(entities, ContentEntityTypes.Map);
            var item = MaxIndex(entities, ContentEntityTypes.Item);
            var monster = MaxIndex(entities, ContentEntityTypes.Monster);
            var npc = MaxIndex(entities, ContentEntityTypes.Npc);
            var quest = MaxIndex(entities, ContentEntityTypes.Quest);
            var gameShop = MaxIndex(entities, ContentEntityTypes.GameShop);
            var conquest = MaxIndex(entities, ContentEntityTypes.Conquest);
            var respawn = MaxRespawnIndex(entities);

            if (legacy.ValueKind != JsonValueKind.Object ||
                !legacy.TryGetProperty("indices", out var indices) ||
                indices.ValueKind != JsonValueKind.Object)
            {
                return new LegacyDatabaseIndices(
                    map, item, monster, npc, quest, gameShop, conquest, respawn);
            }

            return new LegacyDatabaseIndices(
                ReadInt32(indices, "map", map),
                ReadInt32(indices, "item", item),
                ReadInt32(indices, "monster", monster),
                ReadInt32(indices, "npc", npc),
                ReadInt32(indices, "quest", quest),
                ReadInt32(indices, "gameshop", gameShop),
                ReadInt32(indices, "conquest", conquest),
                ReadInt32(indices, "respawn", respawn));
        }

        private static RespawnTickSnapshot ReadRespawnTick(JsonElement legacy)
        {
            if (legacy.ValueKind != JsonValueKind.Object ||
                !legacy.TryGetProperty("respawnTick", out var tick) ||
                tick.ValueKind != JsonValueKind.Object)
            {
                return new RespawnTickSnapshot(
                    BaseSpawnRate: 20,
                    CurrentTickCounter: 0,
                    Options: new[] { new RespawnTickOptionSnapshot(0, 1.0) });
            }

            var options = new List<RespawnTickOptionSnapshot>();
            if (tick.TryGetProperty("options", out var optionsElement) &&
                optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in optionsElement.EnumerateArray())
                {
                    options.Add(new RespawnTickOptionSnapshot(
                        ReadInt32(option, "userCount", 1),
                        ReadDouble(option, "delayLoss", 1.0)));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new RespawnTickOptionSnapshot(0, 1.0));
            }

            return new RespawnTickSnapshot(
                ReadByte(tick, "baseSpawnRate", 20),
                ReadUInt64(tick, "currentTickCounter", 0),
                options);
        }

        private static IReadOnlyList<GTMap> ReadGtMaps(JsonElement legacy)
        {
            if (legacy.ValueKind != JsonValueKind.Object ||
                !legacy.TryGetProperty("gtMaps", out var gtMaps) ||
                gtMaps.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<GTMap>();
            }

            var result = new List<GTMap>();
            foreach (var item in gtMaps.EnumerateArray())
            {
                result.Add(new GTMap
                {
                    Index = ReadInt32(item, "index", 0),
                    Key = ReadInt32(item, "key", 0),
                    Name = ReadString(item, "name"),
                    Owner = ReadString(item, "owner"),
                    Leader = ReadString(item, "leader"),
                    Leader2 = ReadString(item, "leader2"),
                    Price = ReadInt32(item, "price", 0),
                    Days = ReadInt32(item, "days", 0),
                    Begin = ReadInt32(item, "begin", 0)
                });
            }

            return result;
        }

        private static int MaxIndex(IEnumerable<ContentEntity> entities, string entityType)
        {
            var values = entities
                .Where(entity => entity.EntityType == entityType)
                .Select(entity => entity.LocalIndex);
            return (int)Math.Min(int.MaxValue, values.DefaultIfEmpty(0).Max());
        }

        private static int MaxRespawnIndex(IEnumerable<ContentEntity> entities)
        {
            var max = 0;
            foreach (var entity in entities.Where(entity => entity.EntityType == ContentEntityTypes.Respawn))
            {
                if (entity.Data.ValueKind == JsonValueKind.Object &&
                    entity.Data.TryGetProperty("legacyRespawnIndex", out var legacyIndex) &&
                    legacyIndex.TryGetInt32(out var value))
                {
                    max = Math.Max(max, value);
                }
            }

            return max;
        }

        private static int ReadInt32(JsonElement element, string name, int fallback)
        {
            return element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
                ? result
                : fallback;
        }

        private static byte ReadByte(JsonElement element, string name, byte fallback)
        {
            return element.TryGetProperty(name, out var value) && value.TryGetByte(out var result)
                ? result
                : fallback;
        }

        private static ulong ReadUInt64(JsonElement element, string name, ulong fallback)
        {
            return element.TryGetProperty(name, out var value) && value.TryGetUInt64(out var result)
                ? result
                : fallback;
        }

        private static double ReadDouble(JsonElement element, string name, double fallback)
        {
            return element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result)
                ? result
                : fallback;
        }

        private static string ReadString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    private sealed record RespawnTickSnapshot(
        byte BaseSpawnRate,
        ulong CurrentTickCounter,
        IReadOnlyList<RespawnTickOptionSnapshot> Options);

    private sealed record RespawnTickOptionSnapshot(int UserCount, double DelayLoss);

    private sealed class LegacyEntityOrder
    {
        public static LegacyEntityOrder Empty { get; } = new();

        public IReadOnlyList<long>? Maps { get; init; }

        public IReadOnlyList<long>? Items { get; init; }

        public IReadOnlyList<long>? Monsters { get; init; }

        public IReadOnlyList<long>? Npcs { get; init; }

        public IReadOnlyList<long>? Quests { get; init; }

        public IReadOnlyList<long>? Magics { get; init; }

        public IReadOnlyList<long>? GameShop { get; init; }

        public IReadOnlyList<long>? Conquests { get; init; }
    }
}
