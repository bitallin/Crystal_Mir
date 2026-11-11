using System.Text.Json;
using Crystal.Content;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Content;

public sealed class ContentRelationStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ContentRelationStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<MapRelationsDto?> GetMapRelationsAsync(
        long importRunId,
        long mapIndex,
        int respawnPage,
        int respawnPageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var map = await GetByLocalIndexAsync(
            connection,
            importRunId,
            ContentEntityTypes.Map,
            mapIndex,
            cancellationToken);
        if (map is null) return null;

        var movementElements = ReadArray(map.Data, "Movements");
        var targetMapIndices = movementElements
            .Select(movement => ReadInt(movement, "MapIndex"))
            .Where(index => index is not null)
            .Select(index => (long)index!.Value)
            .Distinct()
            .ToArray();
        var targetMaps = await GetByLocalIndicesAsync(
            connection,
            importRunId,
            ContentEntityTypes.Map,
            targetMapIndices,
            cancellationToken);

        var movements = movementElements
            .Select(movement =>
            {
                var targetIndex = ReadInt(movement, "MapIndex");
                return new MapMovementRelationDto(
                    targetIndex ?? 0,
                    targetIndex is not null && targetMaps.TryGetValue(targetIndex.Value, out var target)
                        ? ToMapSummary(target)
                        : null,
                    ReadPoint(movement, "Source"),
                    ReadPoint(movement, "Destination"),
                    ReadBool(movement, "NeedHole"),
                    ReadBool(movement, "NeedMove"),
                    ReadBool(movement, "ShowOnBigMap"),
                    ReadInt(movement, "ConquestIndex") ?? 0,
                    ReadInt(movement, "Icon") ?? 0);
            })
            .ToArray();

        var safeZones = ReadArray(map.Data, "SafeZones")
            .Select(safeZone => new MapSafeZoneRelationDto(
                ReadPoint(safeZone, "Location"),
                ReadInt(safeZone, "Size") ?? 0,
                ReadBool(safeZone, "StartPoint")))
            .ToArray();

        var npcs = (await GetByJsonValueAsync(
                connection,
                importRunId,
                ContentEntityTypes.Npc,
                "MapIndex",
                mapIndex,
                cancellationToken))
            .Select(npc => new MapNpcRelationDto(
                npc.LocalIndex,
                npc.ContentKey,
                ReadString(npc.Data, "Name"),
                ReadString(npc.Data, "FileName"),
                ReadPoint(npc.Data, "Location"),
                ReadInt(npc.Data, "Image"),
                ReadBool(npc.Data, "ShowOnBigMap")))
            .ToArray();

        var respawnTotal = await CountByJsonValueAsync(
            connection,
            importRunId,
            ContentEntityTypes.Respawn,
            "mapIndex",
            mapIndex,
            cancellationToken);
        var respawns = await GetRespawnsPageAsync(
            connection,
            importRunId,
            mapIndex,
            respawnPage,
            respawnPageSize,
            cancellationToken);

        var monsterIndices = respawns
            .Select(respawn => ReadInt(respawn.Data, "MonsterIndex"))
            .Where(index => index is not null)
            .Select(index => (long)index!.Value)
            .Distinct()
            .ToArray();
        var monsters = await GetByLocalIndicesAsync(
            connection,
            importRunId,
            ContentEntityTypes.Monster,
            monsterIndices,
            cancellationToken);

        var respawnDtos = respawns
            .Select(respawn =>
            {
                var monsterIndex = ReadInt(respawn.Data, "MonsterIndex") ?? 0;
                monsters.TryGetValue(monsterIndex, out var monster);
                return new MapRespawnRelationDto(
                    respawn.LocalIndex,
                    respawn.ContentKey,
                    monsterIndex,
                    monster?.ContentKey,
                    monster is null ? null : ReadString(monster.Data, "Name"),
                    ReadPoint(respawn.Data, "Location"),
                    ReadInt(respawn.Data, "Count") ?? 0,
                    ReadInt(respawn.Data, "Spread") ?? 0,
                    ReadInt(respawn.Data, "Delay") ?? 0,
                    ReadInt(respawn.Data, "Direction") ?? 0,
                    ReadInt(respawn.Data, "RandomDelay") ?? 0,
                    ReadString(respawn.Data, "RoutePath"),
                    ReadInt(respawn.Data, "RespawnTicks") ?? 0,
                    ReadInt(respawn.Data, "legacyRespawnIndex"));
            })
            .ToArray();

        return new MapRelationsDto(
            importRunId,
            ToMapSummary(map),
            movements,
            safeZones,
            npcs,
            new MapRespawnPageDto(respawnPage, respawnPageSize, respawnTotal, respawnDtos));
    }

    public async Task<MonsterDropsDto?> GetMonsterDropsAsync(
        long importRunId,
        long monsterIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var monster = await GetByLocalIndexAsync(
            connection,
            importRunId,
            ContentEntityTypes.Monster,
            monsterIndex,
            cancellationToken);
        if (monster is null) return null;

        var drop = await GetByLocalIndexAsync(
            connection,
            importRunId,
            ContentEntityTypes.Drop,
            monsterIndex,
            cancellationToken);
        if (drop is null)
        {
            return new MonsterDropsDto(
                importRunId,
                monster.LocalIndex,
                monster.ContentKey,
                ReadString(monster.Data, "Name"),
                ReadString(monster.Data, "DropPath"),
                null,
                []);
        }

        var itemIndices = new HashSet<long>();
        CollectDropItemIndicesFromEntity(drop.Data, itemIndices);
        var items = await GetByLocalIndicesAsync(
            connection,
            importRunId,
            ContentEntityTypes.Item,
            itemIndices.ToArray(),
            cancellationToken);
        var dropDtos = ReadDrops(drop.Data, items);

        return new MonsterDropsDto(
            importRunId,
            monster.LocalIndex,
            monster.ContentKey,
            ReadString(monster.Data, "Name"),
            ReadString(drop.Data, "dropPath") ?? ReadString(monster.Data, "DropPath"),
            drop.ContentKey,
            dropDtos);
    }

    private static async Task<ContentEntityDto?> GetByLocalIndexAsync(
        NpgsqlConnection connection,
        long importRunId,
        string entityType,
        long localIndex,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId AND entity_type = @entityType AND local_index = @localIndex;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("localIndex", localIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
    }

    private static async Task<Dictionary<long, ContentEntityDto>> GetByLocalIndicesAsync(
        NpgsqlConnection connection,
        long importRunId,
        string entityType,
        IReadOnlyCollection<long> indices,
        CancellationToken cancellationToken)
    {
        if (indices.Count == 0)
        {
            return new Dictionary<long, ContentEntityDto>();
        }

        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = @entityType
                             AND local_index = ANY(@indices)
                           ORDER BY local_index;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.Add(new NpgsqlParameter("indices", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = indices.ToArray()
        });

        var entities = new Dictionary<long, ContentEntityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entity = ReadEntity(reader);
            entities[entity.LocalIndex] = entity;
        }

        return entities;
    }

    private static async Task<IReadOnlyList<ContentEntityDto>> GetByJsonValueAsync(
        NpgsqlConnection connection,
        long importRunId,
        string entityType,
        string field,
        long value,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = @entityType
                             AND data->>@field = @value
                           ORDER BY local_index;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("field", field);
        command.Parameters.AddWithValue("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var entities = new List<ContentEntityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entities.Add(ReadEntity(reader));
        }

        return entities;
    }

    private static async Task<long> CountByJsonValueAsync(
        NpgsqlConnection connection,
        long importRunId,
        string entityType,
        string field,
        long value,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT count(*)::bigint
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = @entityType
                             AND data->>@field = @value;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("field", field);
        command.Parameters.AddWithValue("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<IReadOnlyList<ContentEntityDto>> GetRespawnsPageAsync(
        NpgsqlConnection connection,
        long importRunId,
        long mapIndex,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = 'respawn'
                             AND data->>'mapIndex' = @mapIndex
                           ORDER BY local_index
                           LIMIT @limit OFFSET @offset;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue(
            "mapIndex",
            mapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var entities = new List<ContentEntityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entities.Add(ReadEntity(reader));
        }

        return entities;
    }

    private static IReadOnlyList<DropRelationDto> ReadDrops(
        JsonElement data,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        return ReadArray(data, "drops")
            .Select(drop => ReadDrop(drop, items))
            .ToArray();
    }

    private static DropRelationDto ReadDrop(
        JsonElement drop,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        return new DropRelationDto(
            ReadInt(drop, "Chance") ?? 0,
            ReadUInt(drop, "Gold") ?? 0,
            ReadBool(drop, "QuestRequired"),
            ReadInt(drop, "Type") ?? 0,
            ReadDropItem(drop, items),
            ReadDropGroup(drop, items));
    }

    private static DropItemReferenceDto? ReadDropItem(
        JsonElement drop,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        if (!drop.TryGetProperty("Item", out var item) || item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var index = ReadLong(item, "index");
        if (index is null) return null;
        items.TryGetValue(index.Value, out var itemEntity);
        return new DropItemReferenceDto(
            index.Value,
            ReadString(item, "key") ?? $"item.{index.Value}",
            itemEntity is null ? null : ReadString(itemEntity.Data, "Name"));
    }

    private static DropGroupDto? ReadDropGroup(
        JsonElement drop,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        if (!drop.TryGetProperty("GroupedDrop", out var group) || group.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var children = ReadArray(group, "items")
            .Select(child => ReadDrop(child, items))
            .ToArray();
        return new DropGroupDto(ReadBool(group, "Random"), ReadBool(group, "First"), children);
    }

    private static void CollectDropItemIndicesFromEntity(JsonElement data, ISet<long> indices)
    {
        foreach (var drop in ReadArray(data, "drops"))
        {
            CollectDropItemIndicesFromDrop(drop, indices);
        }
    }

    private static void CollectDropItemIndicesFromDrop(JsonElement drop, ISet<long> indices)
    {
        if (drop.TryGetProperty("Item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            var index = ReadLong(item, "index");
            if (index is not null) indices.Add(index.Value);
        }

        if (drop.TryGetProperty("GroupedDrop", out var group) && group.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in ReadArray(group, "items"))
            {
                CollectDropItemIndicesFromDrop(child, indices);
            }
        }
    }

    private static MapSummaryDto ToMapSummary(ContentEntityDto map)
    {
        return new MapSummaryDto(
            map.LocalIndex,
            map.ContentKey,
            ReadInt(map.Data, "Index") ?? (int)map.LocalIndex,
            ReadString(map.Data, "FileName"),
            ReadString(map.Data, "Title"));
    }

    private static JsonElement[] ReadArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().ToArray();
    }

    private static PointDto? ReadPoint(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var point) ||
            point.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var x = ReadInt(point, "x") ?? ReadInt(point, "X");
        var y = ReadInt(point, "y") ?? ReadInt(point, "Y");
        return x is null || y is null ? null : new PointDto(x.Value, y.Value);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        var value = ReadLong(element, propertyName);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static uint? ReadUInt(JsonElement element, string propertyName)
    {
        var value = ReadLong(element, propertyName);
        return value is >= uint.MinValue and <= uint.MaxValue ? (uint)value.Value : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(
                   value.GetString(),
                   System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out number)
            ? number
            : null;
    }

    private static ContentEntityDto ReadEntity(NpgsqlDataReader reader)
    {
        using var document = JsonDocument.Parse(reader.GetString(5));
        return new ContentEntityDto(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt32(4),
            document.RootElement.Clone());
    }

}
