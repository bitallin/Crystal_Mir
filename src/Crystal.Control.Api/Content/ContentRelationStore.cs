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

        var mapFileName = ReadString(map.Data, "FileName");
        var mapAssetPath = BuildMapAssetPath(mapFileName);
        var mapAsset = mapAssetPath is null
            ? null
            : await GetAssetByRelativePathAsync(
                connection,
                importRunId,
                mapAssetPath,
                cancellationToken);

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

        var npcEntities = await GetByJsonValueAsync(
            connection,
            importRunId,
            ContentEntityTypes.Npc,
            "MapIndex",
            mapIndex,
            cancellationToken);
        var scriptFileNames = npcEntities
            .Select(npc => NormalizeScriptFileName(ReadString(npc.Data, "FileName")))
            .Where(fileName => fileName is not null)
            .Select(fileName => fileName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var scriptsByFileName = await GetByJsonValueListAsync(
            connection,
            importRunId,
            ContentEntityTypes.Script,
            "fileNameKey",
            scriptFileNames,
            cancellationToken);
        var npcs = npcEntities
            .Select(npc =>
            {
                var fileName = NormalizeScriptFileName(ReadString(npc.Data, "FileName"));
                var script = fileName is null ? null : scriptsByFileName.GetValueOrDefault(fileName);
                return new MapNpcRelationDto(
                    npc.LocalIndex,
                    npc.ContentKey,
                    ReadString(npc.Data, "Name"),
                    ReadString(npc.Data, "FileName"),
                    ReadPoint(npc.Data, "Location"),
                    ReadInt(npc.Data, "Image"),
                    ReadBool(npc.Data, "ShowOnBigMap"),
                    ToScriptReference(script));
            })
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
            ToMapAssetReference(mapAsset),
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

    public async Task<NpcScriptRelationsDto?> GetNpcScriptAsync(
        long importRunId,
        long npcIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var npc = await GetByLocalIndexAsync(
            connection,
            importRunId,
            ContentEntityTypes.Npc,
            npcIndex,
            cancellationToken);
        if (npc is null) return null;

        var fileName = NormalizeScriptFileName(ReadString(npc.Data, "FileName"));
        var script = fileName is null
            ? null
            : await GetByJsonValueStringAsync(
                connection,
                importRunId,
                ContentEntityTypes.Script,
                "fileNameKey",
                fileName,
                cancellationToken);
        return ToNpcScriptRelations(importRunId, npc, script);
    }

    public async Task<NpcScriptValidationDto?> GetScriptValidationAsync(
        long importRunId,
        long scriptIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var script = await GetByLocalIndexAsync(
            connection,
            importRunId,
            ContentEntityTypes.Script,
            scriptIndex,
            cancellationToken);
        if (script is null) return null;

        var validation = ReadValidationSummary(script.Data);
        var issues = ReadValidationIssues(script.Data);
        NpcScriptAst? ast = null;
        if (script.Data.ValueKind == JsonValueKind.Object &&
            script.Data.TryGetProperty("ast", out var astElement) &&
            astElement.ValueKind == JsonValueKind.Object)
        {
            ast = JsonSerializer.Deserialize<NpcScriptAst>(
                astElement.GetRawText(),
                ContentJson.CompactOptions);
        }

        return new NpcScriptValidationDto(
            validation is not null,
            importRunId,
            script.LocalIndex,
            script.ContentKey,
            ReadString(script.Data, "fileName"),
            ReadString(script.Data, "relativePath"),
            ReadString(script.Data, "sha256"),
            validation ?? new NpcScriptValidationSummaryDto(false, 0, 0, null),
            issues,
            ast);
    }

    public async Task<RecipeRelationsDto?> GetRecipeRelationsAsync(
        long importRunId,
        long recipeIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var recipe = await GetByLocalIndexAsync(
            connection,
            importRunId,
            ContentEntityTypes.Recipe,
            recipeIndex,
            cancellationToken);
        if (recipe is null) return null;

        var itemIndices = new HashSet<long>();
        CollectRecipeItemIndicesFromEntity(recipe.Data, itemIndices);
        var items = await GetByLocalIndicesAsync(
            connection,
            importRunId,
            ContentEntityTypes.Item,
            itemIndices.ToArray(),
            cancellationToken);

        return new RecipeRelationsDto(
            importRunId,
            recipe.LocalIndex,
            recipe.ContentKey,
            ReadString(recipe.Data, "name"),
            ReadString(recipe.Data, "fileName"),
            ReadRecipeItem(recipe.Data, "output", items),
            ReadRecipeItems(recipe.Data, "ingredients", items),
            ReadRecipeItems(recipe.Data, "tools", items),
            ReadInt(recipe.Data, "chance") ?? 0,
            ReadUInt(recipe.Data, "gold") ?? 0,
            ReadInt(recipe.Data, "requiredLevel"),
            ReadString(recipe.Data, "requiredGender"),
            ReadStringArray(recipe.Data, "requiredClasses"),
            ReadIntArray(recipe.Data, "requiredFlags"),
            ReadIntArray(recipe.Data, "requiredQuests"));
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

    private static async Task<Dictionary<string, ContentEntityDto>> GetByJsonValueListAsync(
        NpgsqlConnection connection,
        long importRunId,
        string entityType,
        string field,
        IReadOnlyCollection<string> values,
        CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return new Dictionary<string, ContentEntityDto>(StringComparer.Ordinal);
        }

        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = @entityType
                             AND data->>@field = ANY(@values)
                           ORDER BY local_index;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("field", field);
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = values.ToArray()
        });

        var entities = new Dictionary<string, ContentEntityDto>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entity = ReadEntity(reader);
            var key = ReadString(entity.Data, field);
            if (key is not null)
            {
                entities[key] = entity;
            }
        }

        return entities;
    }

    private static async Task<ContentEntityDto?> GetByJsonValueStringAsync(
        NpgsqlConnection connection,
        long importRunId,
        string entityType,
        string field,
        string value,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = @entityType
                             AND data->>@field = @value
                           ORDER BY local_index
                           LIMIT 1;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("field", field);
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
    }

    private static async Task<ContentEntityDto?> GetAssetByRelativePathAsync(
        NpgsqlConnection connection,
        long importRunId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId
                             AND entity_type = 'asset'
                             AND lower(data->>'relativePath') = lower(@relativePath)
                           ORDER BY local_index
                           LIMIT 1;
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("relativePath", relativePath);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
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

    private static RecipeItemReferenceDto? ReadRecipeItem(
        JsonElement data,
        string propertyName,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(propertyName, out var item) ||
            item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadRecipeItemValue(item, items);
    }

    private static IReadOnlyList<RecipeItemReferenceDto> ReadRecipeItems(
        JsonElement data,
        string propertyName,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        return ReadArray(data, propertyName)
            .Select(item => ReadRecipeItemValue(item, items))
            .Where(item => item is not null)
            .Cast<RecipeItemReferenceDto>()
            .ToArray();
    }

    private static RecipeItemReferenceDto? ReadRecipeItemValue(
        JsonElement item,
        IReadOnlyDictionary<long, ContentEntityDto> items)
    {
        var itemIndex = ReadInt(item, "itemIndex");
        if (itemIndex is null) return null;

        items.TryGetValue(itemIndex.Value, out var itemEntity);
        return new RecipeItemReferenceDto(
            itemEntity?.LocalIndex,
            itemEntity?.ContentKey,
            itemIndex.Value,
            itemEntity is null ? ReadString(item, "itemName") : ReadString(itemEntity.Data, "Name"),
            ReadInt(item, "count") ?? 0,
            ReadInt(item, "currentDura") ?? 0,
            ReadInt(item, "maxDura") ?? 0);
    }

    private static void CollectRecipeItemIndicesFromEntity(JsonElement data, ISet<long> indices)
    {
        foreach (var propertyName in new[] { "output", "ingredients", "tools" })
        {
            if (propertyName == "output")
            {
                CollectRecipeItemIndex(data, propertyName, indices);
                continue;
            }

            foreach (var item in ReadArray(data, propertyName))
            {
                CollectRecipeItemIndex(item, indices);
            }
        }
    }

    private static void CollectRecipeItemIndex(JsonElement item, ISet<long> indices)
    {
        var itemIndex = ReadInt(item, "itemIndex");
        if (itemIndex is not null) indices.Add(itemIndex.Value);
    }

    private static void CollectRecipeItemIndex(
        JsonElement data,
        string propertyName,
        ISet<long> indices)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(propertyName, out var item) ||
            item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        CollectRecipeItemIndex(item, indices);
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

    private static string? NormalizeScriptFileName(string? fileName)
    {
        return fileName?.Trim().Replace('\\', '/').Trim('/').ToLowerInvariant();
    }

    private static MapNpcScriptReferenceDto? ToScriptReference(ContentEntityDto? script)
    {
        return script is null
            ? null
            : new MapNpcScriptReferenceDto(
                script.LocalIndex,
                script.ContentKey,
                ReadString(script.Data, "fileName"),
                ReadString(script.Data, "relativePath"),
                ReadInt(script.Data, "lineCount"),
                ReadArray(script.Data, "pages").Length);
    }

    private static NpcScriptRelationsDto ToNpcScriptRelations(
        long importRunId,
        ContentEntityDto npc,
        ContentEntityDto? script)
    {
        return new NpcScriptRelationsDto(
            importRunId,
            npc.LocalIndex,
            npc.ContentKey,
            ReadString(npc.Data, "Name"),
            ReadString(npc.Data, "FileName"),
            script is not null,
            script?.LocalIndex,
            script?.ContentKey,
            script is null ? null : ReadString(script.Data, "relativePath"),
            script is null ? null : ReadString(script.Data, "language"),
            script is null ? null : ReadString(script.Data, "sha256"),
            script is null ? null : ReadInt(script.Data, "sizeBytes"),
            script is null ? null : ReadInt(script.Data, "lineCount"),
            script is null ? [] : ReadStringArray(script.Data, "pages"),
            script is null
                ? []
                : ReadArray(script.Data, "includes")
                    .Select(include => new NpcScriptIncludeDto(
                        ReadString(include, "path") ?? string.Empty,
                        ReadString(include, "page") ?? string.Empty))
                    .ToArray(),
            script is null ? [] : ReadStringArray(script.Data, "inserts"),
            script is null ? [] : ReadStringArray(script.Data, "calls"),
            script is null ? null : ReadValidationSummary(script.Data),
            script is null ? null : ReadString(script.Data, "source"));
    }

    private static NpcScriptValidationSummaryDto? ReadValidationSummary(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("validation", out var validation) ||
            validation.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new NpcScriptValidationSummaryDto(
            ReadBool(validation, "valid"),
            ReadInt(validation, "errorCount") ?? 0,
            ReadInt(validation, "warningCount") ?? 0,
            ReadString(validation, "compiledSha256"));
    }

    private static IReadOnlyList<NpcScriptValidationIssueDto> ReadValidationIssues(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("validation", out var validation) ||
            validation.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return ReadArray(validation, "issues")
            .Select(issue => new NpcScriptValidationIssueDto(
                ReadString(issue, "code") ?? "unknown",
                ReadString(issue, "severity") ?? "error",
                ReadString(issue, "message") ?? string.Empty,
                ReadInt(issue, "line"),
                ReadString(issue, "page")))
            .ToArray();
    }

    private static MapAssetReferenceDto? ToMapAssetReference(ContentEntityDto? asset)
    {
        if (asset is null) return null;

        MapAssetMetadata? map = null;
        if (asset.Data.ValueKind == JsonValueKind.Object &&
            asset.Data.TryGetProperty("map", out var mapElement) &&
            mapElement.ValueKind == JsonValueKind.Object)
        {
            map = JsonSerializer.Deserialize<MapAssetMetadata>(
                mapElement.GetRawText(),
                ContentJson.CompactOptions);
        }

        return new MapAssetReferenceDto(
            asset.LocalIndex,
            asset.ContentKey,
            ReadString(asset.Data, "relativePath"),
            ReadLong(asset.Data, "sizeBytes") ?? 0,
            ReadString(asset.Data, "sha256"),
            map);
    }

    private static string? BuildMapAssetPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var normalized = fileName.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Contains("..", StringComparison.Ordinal)) return null;
        return normalized.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
            ? $"Maps/{normalized}"
            : $"Maps/{normalized}.map";
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

    private static IReadOnlyList<int> ReadIntArray(JsonElement element, string propertyName)
    {
        return ReadArray(element, propertyName)
            .Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : (int?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        return ReadArray(element, propertyName)
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
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
