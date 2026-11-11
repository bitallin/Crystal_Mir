using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Crystal.LegacyImporter;

public sealed class LegacyContentMappingResult
{
    public required IReadOnlyList<ContentEntity> Entities { get; init; }

    public required SortedDictionary<string, int> SourceCounts { get; init; }

    public required JsonElement Legacy { get; init; }
}

public static class LegacyContentMapper
{
    public static LegacyContentMappingResult Map(
        Envir envir,
        LegacyDatabaseSession session,
        string? contentRoot = null)
    {
        var entities = new List<ContentEntity>();
        var sourceCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        AddEntities(entities, sourceCounts, ContentEntityTypes.Item, envir.ItemInfoList,
            item => item.Index, item => $"item.{item.Index}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.Monster, envir.MonsterInfoList,
            monster => monster.Index, monster => $"monster.{monster.Index}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.Map, envir.MapInfoList,
            map => map.Index, map => $"map.{map.Index}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.Npc, envir.NPCInfoList,
            npc => npc.Index, npc => $"npc.{npc.Index}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.Quest, envir.QuestInfoList,
            quest => quest.Index, quest => $"quest.{quest.Index}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.Magic, envir.MagicInfoList,
            magic => (long)magic.Spell, magic => $"magic.{(long)magic.Spell}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.GameShop, envir.GameShopList,
            item => item.GIndex, item => $"gameshop.{item.GIndex}");
        AddEntities(entities, sourceCounts, ContentEntityTypes.Conquest, envir.ConquestInfoList,
            conquest => conquest.Index, conquest => $"conquest.{conquest.Index}");

        if (envir.DragonInfo is not null)
        {
            sourceCounts[ContentEntityTypes.Dragon] = 1;
            entities.Add(new ContentEntity
            {
                EntityType = ContentEntityTypes.Dragon,
                LocalIndex = 1,
                ContentKey = "dragon.default",
                Data = LegacyJsonSerializer.Serialize(envir.DragonInfo)
            });
        }

        var respawns = envir.MapInfoList
            .SelectMany(map => map.Respawns.Select((respawn, ordinal) => (Map: map, Respawn: respawn, Ordinal: ordinal)))
            .ToList();
        sourceCounts[ContentEntityTypes.Respawn] = respawns.Count;
        foreach (var (map, respawn, ordinal) in respawns)
        {
            entities.Add(CreateRespawnEntity(map, respawn, ordinal));
        }

        DropContentImportResult? drops = null;
        if (contentRoot is not null)
        {
            drops = DropContentImporter.Map(envir, contentRoot);
            sourceCounts[ContentEntityTypes.Drop] = drops.Entities.Count;
            entities.AddRange(drops.Entities);
        }

        var legacy = ContentJson.Canonicalize(JsonSerializer.SerializeToElement(new
        {
            source = session.SourcePath,
            sourceHash = session.SourceHash,
            headerVersion = session.HeaderVersion,
            headerCustomVersion = session.HeaderCustomVersion,
            loadVersion = Envir.LoadVersion,
            loadCustomVersion = Envir.LoadCustomVersion,
            currentVersion = Envir.Version,
            indices = new
            {
                map = envir.MapIndex,
                item = envir.ItemIndex,
                monster = envir.MonsterIndex,
                npc = envir.NPCIndex,
                quest = envir.QuestIndex,
                gameshop = envir.GameshopIndex,
                conquest = envir.ConquestIndex,
                respawn = envir.RespawnIndex
            },
            counts = new
            {
                maps = envir.MapInfoList.Count,
                items = envir.ItemInfoList.Count,
                monsters = envir.MonsterInfoList.Count,
                magics = envir.MagicInfoList.Count,
                npcs = envir.NPCInfoList.Count,
                quests = envir.QuestInfoList.Count,
                gameshop = envir.GameShopList.Count,
                conquests = envir.ConquestInfoList.Count,
                respawns = respawns.Count
            },
            contentRoot,
            drops = drops is null
                ? null
                : new
                {
                    files = drops.FileCount,
                    entries = drops.DropCount,
                    entities = drops.Entities.Count
                },
            order = new
            {
                maps = envir.MapInfoList.Select(map => map.Index).ToArray(),
                items = envir.ItemInfoList.Select(item => item.Index).ToArray(),
                monsters = envir.MonsterInfoList.Select(monster => monster.Index).ToArray(),
                npcs = envir.NPCInfoList.Select(npc => npc.Index).ToArray(),
                quests = envir.QuestInfoList.Select(quest => quest.Index).ToArray(),
                magics = envir.MagicInfoList.Select(magic => (long)magic.Spell).ToArray(),
                gameshop = envir.GameShopList.Select(item => item.GIndex).ToArray(),
                conquests = envir.ConquestInfoList.Select(conquest => conquest.Index).ToArray()
            },
            respawnTick = new
            {
                baseSpawnRate = envir.RespawnTick.BaseSpawnRate,
                currentTickCounter = envir.RespawnTick.CurrentTickcounter,
                options = envir.RespawnTick.Respawn.Select(option => new
                {
                    userCount = option.UserCount,
                    delayLoss = option.DelayLoss
                }).ToArray()
            },
            gtMaps = envir.GTMapList.Select(gtMap => new
            {
                index = gtMap.Index,
                key = gtMap.Key,
                name = gtMap.Name,
                owner = gtMap.Owner,
                leader = gtMap.Leader,
                leader2 = gtMap.Leader2,
                price = gtMap.Price,
                days = gtMap.Days,
                begin = gtMap.Begin
            }).ToArray(),
            notImported = new
            {
                recipes = "loaded from Envir/Recipe, outside Server.MirDB",
                buffs = "initialized during server startup, outside Server.MirDB",
                drops = drops is null
                    ? "not loaded; pass --content-root to import Envir/Drops"
                    : null
            }
        }, ContentJson.CompactOptions));

        return new LegacyContentMappingResult
        {
            Entities = entities,
            SourceCounts = sourceCounts,
            Legacy = legacy
        };
    }

    public static ContentEntity CreateRespawnEntity(MapInfo map, RespawnInfo respawn, int ordinal)
    {
        // RespawnIndex is not globally unique in legacy databases; use map + ordinal as the stable local identity.
        var localIndex = (long)map.Index * 1_000_000L + ordinal + 1L;
        var payload = LegacyJsonSerializer.Serialize(respawn);
        var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
        node["mapIndex"] = map.Index;
        node["mapFileName"] = map.FileName;
        node["legacyRespawnIndex"] = respawn.RespawnIndex;
        return new ContentEntity
        {
            EntityType = ContentEntityTypes.Respawn,
            LocalIndex = localIndex,
            ContentKey = $"respawn.map{map.Index}.{ordinal + 1}",
            Data = ContentJson.Canonicalize(JsonSerializer.SerializeToElement(node))
        };
    }

    private static void AddEntities<T>(
        ICollection<ContentEntity> entities,
        IDictionary<string, int> sourceCounts,
        string entityType,
        IEnumerable<T> source,
        Func<T, long> indexSelector,
        Func<T, string> keySelector)
    {
        var list = source.ToList();
        sourceCounts[entityType] = list.Count;
        foreach (var value in list)
        {
            var index = indexSelector(value);
            entities.Add(new ContentEntity
            {
                EntityType = entityType,
                LocalIndex = index,
                ContentKey = keySelector(value),
                Data = LegacyJsonSerializer.Serialize(value!)
            });
        }
    }
}
