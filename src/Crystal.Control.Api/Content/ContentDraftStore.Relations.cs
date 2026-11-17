using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Content;

public sealed partial class ContentDraftStore
{
    private static readonly IReadOnlyDictionary<string, string> MapArrayProperties =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["movements"] = "Movements",
            ["safezones"] = "SafeZones",
            ["minezones"] = "MineZones",
            ["activecoords"] = "ActiveCoords"
        };

    public async Task<DraftMapRelationsDto> GetMapRelationsAsync(
        long draftId,
        long mapIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, local_index, content_key, schema_version, data::text,
                   revision, updated_by, updated_at
            FROM content_draft_entity
            WHERE draft_id = @draftId
            ORDER BY entity_type, local_index;
            """,
            connection);
        command.Parameters.AddWithValue("draftId", draftId);

        JsonElement? mapData = null;
        long mapRevision = 0;
        string? mapKey = null;
        var respawns = new List<DraftEntityDto>();
        var npcs = new List<DraftEntityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entityType = reader.GetString(0);
            using var document = JsonDocument.Parse(reader.GetString(4));
            var data = document.RootElement.Clone();
            if (entityType == ContentEntityTypes.Map && reader.GetInt64(1) == mapIndex)
            {
                mapData = data;
                mapKey = reader.GetString(2);
                mapRevision = reader.GetInt64(5);
                continue;
            }

            var related = entityType switch
            {
                ContentEntityTypes.Respawn => ReadLong(data, "mapIndex") == mapIndex,
                ContentEntityTypes.Npc => ReadLong(data, "MapIndex") == mapIndex,
                _ => false
            };
            if (!related) continue;

            var entity = new DraftEntityDto(
                draftId,
                entityType,
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                data,
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.GetDateTime(7));
            if (entityType == ContentEntityTypes.Respawn) respawns.Add(entity);
            else npcs.Add(entity);
        }

        if (mapData is null)
        {
            throw new DraftNotFoundException($"Map {mapIndex} does not exist in draft {draftId}.");
        }

        return new DraftMapRelationsDto(
            draftId,
            mapIndex,
            mapKey!,
            mapRevision,
            ReadArray(mapData.Value, "Movements"),
            ReadArray(mapData.Value, "SafeZones"),
            ReadArray(mapData.Value, "MineZones"),
            ReadArray(mapData.Value, "ActiveCoords"),
            respawns.OrderBy(entity => entity.LocalIndex).ToArray(),
            npcs.OrderBy(entity => entity.LocalIndex).ToArray());
    }

    public async Task<DraftEntityDto> ReplaceMapArrayAsync(
        long draftId,
        long mapIndex,
        string property,
        JsonElement items,
        long expectedRevision,
        string? reason,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        if (!MapArrayProperties.TryGetValue(property, out var mapProperty))
        {
            throw new DraftConflictException($"Unknown map relation group '{property}'.");
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            throw new DraftInputValidationException(new Dictionary<string, string[]>
            {
                ["items"] = ["items must be an array."]
            });
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var map = await ReadMapRowForUpdateAsync(connection, transaction, draftId, mapIndex, cancellationToken);
        EnsureMapRevision(map, expectedRevision);
        var context = await LoadRelationContextAsync(connection, transaction, draftId, cancellationToken);
        ValidateMapArray(mapProperty, items, mapIndex, map.FileName, context);

        var node = ParseObject(map.Data);
        node[mapProperty] = JsonNode.Parse(items.GetRawText());
        if (mapProperty == "SafeZones" && node[mapProperty] is JsonArray safeZones)
        {
            foreach (var safeZone in safeZones.OfType<JsonObject>())
            {
                safeZone["Info"] = ContentEditorJson.Reference("map", mapIndex, $"map.{mapIndex}");
            }
        }

        var updatedData = Serialize(node);
        var now = DateTimeOffset.UtcNow;
        await UpdateMapProjectionAsync(connection, transaction, draftId, mapIndex, map.LocalIndex, map.ContentKey,
            map.Data, updatedData, map.Revision, actorAccountId, now, cancellationToken);
        await InsertRevisionAsync(connection, transaction, draftId, "update", "map", map.LocalIndex,
            map.ContentKey, map.Data, updatedData, actorAccountId, reason, now, cancellationToken);
        await TouchDraftAsync(connection, transaction, draftId, actorAccountId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetEntityAsync(draftId, "map", mapIndex, cancellationToken)
               ?? throw new DraftNotFoundException($"map/{mapIndex} disappeared after update.");
    }

    public async Task<DraftEntityDto> SaveMapRelationEntityAsync(
        long draftId,
        long mapIndex,
        string entityType,
        long? localIndex,
        long? expectedRevision,
        JsonElement data,
        string? reason,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        if (entityType is not (ContentEntityTypes.Respawn or ContentEntityTypes.Npc))
        {
            throw new DraftConflictException($"Unsupported map relation entity type '{entityType}'.");
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new DraftInputValidationException(new Dictionary<string, string[]>
            {
                ["data"] = ["data must be a JSON object."]
            });
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var map = await ReadMapRowForUpdateAsync(connection, transaction, draftId, mapIndex, cancellationToken);
        var context = await LoadRelationContextAsync(connection, transaction, draftId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var isCreate = localIndex is null;
        long entityIndex;
        string contentKey;
        long revision = 0;
        JsonElement? beforeData = null;

        var node = ParseObject(data);
        if (entityType == ContentEntityTypes.Respawn)
        {
            node["mapIndex"] = mapIndex;
            node["mapFileName"] = map.FileName;
        }
        else
        {
            node["MapIndex"] = mapIndex;
        }

        if (isCreate)
        {
            entityIndex = await NextLocalIndexAsync(connection, transaction, draftId, entityType, cancellationToken);
            if (entityType == ContentEntityTypes.Npc)
            {
                node["Index"] = entityIndex;
                contentKey = $"npc.{entityIndex}";
            }
            else
            {
                var ordinal = await NextRespawnOrdinalAsync(connection, transaction, draftId, mapIndex, cancellationToken);
                node["RespawnIndex"] = ordinal;
                node["legacyRespawnIndex"] = ordinal;
                contentKey = $"respawn.map{mapIndex}.{ordinal}";
            }
        }
        else
        {
            entityIndex = localIndex
                          ?? throw new DraftConflictException($"{entityType} update requires a local index.");
            var existing = await ReadEntityRowForUpdateAsync(
                connection, transaction, draftId, entityType, entityIndex, cancellationToken);
            if (existing is null)
            {
                throw new DraftNotFoundException($"{entityType}/{entityIndex} does not exist in draft {draftId}.");
            }

            if (ReadLong(existing.Value.Data, "mapIndex") != mapIndex &&
                ReadLong(existing.Value.Data, "MapIndex") != mapIndex)
            {
                throw new DraftConflictException($"{entityType}/{entityIndex} does not belong to map {mapIndex}.");
            }

            if (expectedRevision is null || expectedRevision != existing.Value.Revision)
            {
                throw new DraftConflictException(
                    $"{entityType}/{entityIndex} has revision {existing.Value.Revision}; expected {expectedRevision}.");
            }

            beforeData = existing.Value.Data;
            contentKey = existing.Value.ContentKey;
            revision = existing.Value.Revision + 1;
            if (entityType == ContentEntityTypes.Npc) node["Index"] = entityIndex;
        }

        var normalizedData = Serialize(node);
        ValidateMapRelationEntity(entityType, normalizedData, mapIndex, map.FileName, context);
        if (isCreate)
        {
            revision = 1;
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO content_draft_entity
                    (draft_id, entity_type, local_index, content_key, schema_version, data,
                     revision, updated_by, updated_at)
                VALUES
                    (@draftId, @entityType, @localIndex, @contentKey, 1, @data, 1, @actor, @occurredAt);
                """,
                connection,
                transaction);
            AddEntityParameters(insert, draftId, entityType, entityIndex, contentKey, 1,
                normalizedData, 1, actorAccountId, now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await InsertRevisionAsync(connection, transaction, draftId, "create", entityType,
                entityIndex, contentKey, null, normalizedData, actorAccountId, reason, now, cancellationToken);
        }
        else
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE content_draft_entity
                SET data = @data, revision = @revision, updated_by = @actor, updated_at = @occurredAt
                WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex;
                """,
                connection,
                transaction);
            update.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
            {
                Value = normalizedData.GetRawText()
            });
            update.Parameters.AddWithValue("revision", revision);
            update.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
            {
                Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
            });
            update.Parameters.AddWithValue("occurredAt", now.UtcDateTime);
            update.Parameters.AddWithValue("draftId", draftId);
            update.Parameters.AddWithValue("entityType", entityType);
            update.Parameters.AddWithValue("localIndex", entityIndex);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await InsertRevisionAsync(connection, transaction, draftId, "update", entityType,
                entityIndex, contentKey, beforeData, normalizedData, actorAccountId, reason, now, cancellationToken);
        }

        await RebuildMapProjectionAsync(connection, transaction, draftId, mapIndex, map, actorAccountId, now, cancellationToken);
        await TouchDraftAsync(connection, transaction, draftId, actorAccountId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetEntityAsync(draftId, entityType, entityIndex, cancellationToken)
               ?? throw new DraftNotFoundException($"{entityType}/{entityIndex} disappeared after save.");
    }

    public async Task DeleteMapRelationEntityAsync(
        long draftId,
        long mapIndex,
        string entityType,
        long localIndex,
        long expectedRevision,
        string? reason,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        if (entityType is not (ContentEntityTypes.Respawn or ContentEntityTypes.Npc))
        {
            throw new DraftConflictException($"Unsupported map relation entity type '{entityType}'.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var map = await ReadMapRowForUpdateAsync(connection, transaction, draftId, mapIndex, cancellationToken);
        var existing = await ReadEntityRowForUpdateAsync(
            connection, transaction, draftId, entityType, localIndex, cancellationToken);
        if (existing is null)
        {
            throw new DraftNotFoundException($"{entityType}/{localIndex} does not exist in draft {draftId}.");
        }

        if (ReadLong(existing.Value.Data, "mapIndex") != mapIndex &&
            ReadLong(existing.Value.Data, "MapIndex") != mapIndex)
        {
            throw new DraftConflictException($"{entityType}/{localIndex} does not belong to map {mapIndex}.");
        }

        if (existing.Value.Revision != expectedRevision)
        {
            throw new DraftConflictException(
                $"{entityType}/{localIndex} has revision {existing.Value.Revision}; expected {expectedRevision}.");
        }

        var now = DateTimeOffset.UtcNow;
        await using (var delete = new NpgsqlCommand(
                         """
                         DELETE FROM content_draft_entity
                         WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex;
                         """,
                         connection,
                         transaction))
        {
            delete.Parameters.AddWithValue("draftId", draftId);
            delete.Parameters.AddWithValue("entityType", entityType);
            delete.Parameters.AddWithValue("localIndex", localIndex);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertRevisionAsync(connection, transaction, draftId, "delete", entityType,
            localIndex, existing.Value.ContentKey, existing.Value.Data, null, actorAccountId, reason, now,
            cancellationToken);
        await RebuildMapProjectionAsync(connection, transaction, draftId, mapIndex, map, actorAccountId, now, cancellationToken);
        await TouchDraftAsync(connection, transaction, draftId, actorAccountId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RebuildMapProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        long mapIndex,
        MapRow map,
        long? actorAccountId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var node = ParseObject(map.Data);
        foreach (var (entityType, property, keyName) in new[]
                 {
                     (ContentEntityTypes.Respawn, "Respawns", "mapIndex"),
                     (ContentEntityTypes.Npc, "NPCs", "MapIndex")
                 })
        {
            var rows = new JsonArray();
            await using var command = new NpgsqlCommand(
                """
                SELECT data::text
                FROM content_draft_entity
                WHERE draft_id = @draftId AND entity_type = @entityType
                  AND data ? @mapProperty
                  AND (data->>@mapProperty) ~ '^[0-9]+$'
                  AND (data->>@mapProperty)::int = @mapIndex
                ORDER BY local_index;
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("draftId", draftId);
            command.Parameters.AddWithValue("entityType", entityType);
            command.Parameters.AddWithValue("mapProperty", keyName);
            command.Parameters.AddWithValue("mapIndex", mapIndex);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = JsonNode.Parse(reader.GetString(0))?.AsObject();
                if (item is null) continue;
                if (entityType == ContentEntityTypes.Respawn)
                {
                    item.Remove("mapIndex");
                    item.Remove("mapFileName");
                    item.Remove("legacyRespawnIndex");
                }

                rows.Add(item);
            }

            node[property] = rows;
        }

        var data = Serialize(node);
        await UpdateMapProjectionAsync(connection, transaction, draftId, mapIndex, map.LocalIndex, map.ContentKey,
            map.Data, data, map.Revision, actorAccountId, occurredAt, cancellationToken);
        await InsertRevisionAsync(connection, transaction, draftId, "update", "map", map.LocalIndex,
            map.ContentKey, map.Data, data, actorAccountId, null, occurredAt, cancellationToken);
    }

    private static async Task UpdateMapProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        long mapIndex,
        long localIndex,
        string contentKey,
        JsonElement before,
        JsonElement after,
        long revision,
        long? actorAccountId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE content_draft_entity
            SET data = @data, revision = @revision, updated_by = @actor, updated_at = @occurredAt
            WHERE draft_id = @draftId AND entity_type = 'map' AND local_index = @localIndex;
            """,
            connection,
            transaction);
        update.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = after.GetRawText() });
        update.Parameters.AddWithValue("revision", revision + 1);
        update.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
        {
            Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
        });
        update.Parameters.AddWithValue("occurredAt", occurredAt.UtcDateTime);
        update.Parameters.AddWithValue("draftId", draftId);
        update.Parameters.AddWithValue("localIndex", localIndex);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<MapRow> ReadMapRowForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        long mapIndex,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT local_index, content_key, data::text, revision
            FROM content_draft_entity
            WHERE draft_id = @draftId AND entity_type = 'map' AND local_index = @mapIndex
            FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("mapIndex", mapIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new DraftNotFoundException($"Map {mapIndex} does not exist in draft {draftId}.");
        }

        using var document = JsonDocument.Parse(reader.GetString(2));
        return new MapRow(
            reader.GetInt64(0),
            reader.GetString(1),
            document.RootElement.Clone(),
            reader.GetInt64(3),
            ReadString(document.RootElement, "FileName") ?? string.Empty);
    }

    private static void EnsureMapRevision(MapRow map, long expectedRevision)
    {
        if (map.Revision != expectedRevision)
        {
            throw new DraftConflictException(
                $"Map {map.LocalIndex} has revision {map.Revision}; expected {expectedRevision}.");
        }
    }

    private static async Task<(JsonElement Data, string ContentKey, long Revision)?> ReadEntityRowForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        string entityType,
        long localIndex,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT data::text, content_key, revision
            FROM content_draft_entity
            WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex
            FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("localIndex", localIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        using var document = JsonDocument.Parse(reader.GetString(0));
        return (document.RootElement.Clone(), reader.GetString(1), reader.GetInt64(2));
    }

    private static async Task<long> NextLocalIndexAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        string entityType,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COALESCE(max(local_index), 0) + 1
            FROM content_draft_entity
            WHERE draft_id = @draftId AND entity_type = @entityType;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("entityType", entityType);
        var index = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        if (index > int.MaxValue)
        {
            throw new DraftConflictException($"No {entityType} local index is available.");
        }

        return index;
    }

    private static async Task<int> NextRespawnOrdinalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        long mapIndex,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COALESCE(max(COALESCE((data->>'legacyRespawnIndex')::int, (data->>'RespawnIndex')::int, 0)), 0) + 1
            FROM content_draft_entity
            WHERE draft_id = @draftId AND entity_type = 'respawn'
              AND data ? 'mapIndex'
              AND (data->>'mapIndex') ~ '^[0-9]+$'
              AND (data->>'mapIndex')::int = @mapIndex;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("mapIndex", mapIndex);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<RelationContext> LoadRelationContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, local_index, content_key, data::text
            FROM content_draft_entity
            WHERE draft_id = @draftId
              AND entity_type IN ('map', 'monster', 'npc', 'script', 'asset');
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("draftId", draftId);
        var context = new RelationContext();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(0);
            var index = reader.GetInt64(1);
            var key = reader.GetString(2);
            using var document = JsonDocument.Parse(reader.GetString(3));
            var data = document.RootElement.Clone();
            switch (type)
            {
                case ContentEntityTypes.Map:
                    context.MapIndices.Add(index);
                    context.MapFileNames[index] = ReadString(data, "FileName") ?? string.Empty;
                    break;
                case ContentEntityTypes.Monster:
                    context.MonsterIndices.Add(index);
                    break;
                case ContentEntityTypes.Npc:
                    context.NpcIndices.Add(index);
                    break;
                case ContentEntityTypes.Script:
                    foreach (var scriptKey in ReadScriptKeys(data, key))
                    {
                        context.ScriptKeys.Add(scriptKey);
                    }

                    break;
                case ContentEntityTypes.Asset:
                    if (ReadString(data, "assetType") == "map" &&
                        ReadString(data, "fileName") is { Length: > 0 } fileName &&
                        data.TryGetProperty("map", out var map) && map.ValueKind == JsonValueKind.Object)
                    {
                        var width = ReadLong(map, "width") ?? 0;
                        var height = ReadLong(map, "height") ?? 0;
                        context.MapBounds[fileName] = (width, height);
                    }

                    break;
            }
        }

        return context;
    }

    private static void ValidateMapArray(
        string property,
        JsonElement items,
        long mapIndex,
        string mapFileName,
        RelationContext context)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var path = $"{property}[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors[path] = [$"{path} must be an object."];
                index++;
                continue;
            }

            switch (property)
            {
                case "Movements":
                {
                    var targetMap = ReadLong(item, "MapIndex") ?? 0;
                    if (!context.MapIndices.Contains(targetMap))
                    {
                        errors[$"{path}.MapIndex"] = [$"{path}.MapIndex references missing map {targetMap}."];
                        break;
                    }

                    ValidatePoint(item, "Source", mapFileName, context, errors, path);
                    ValidatePoint(item, "Destination", context.MapFileNames[targetMap], context, errors, path);
                    break;
                }
                case "SafeZones":
                    ValidatePoint(item, "Location", mapFileName, context, errors, path);
                    break;
                case "MineZones":
                    ValidatePoint(item, "Location", mapFileName, context, errors, path);
                    break;
                case "ActiveCoords":
                    ValidatePoint(item, string.Empty, mapFileName, context, errors, path);
                    break;
            }

            index++;
        }

        if (errors.Count > 0)
        {
            throw new DraftInputValidationException(errors);
        }
    }

    private static void ValidateMapRelationEntity(
        string entityType,
        JsonElement data,
        long mapIndex,
        string mapFileName,
        RelationContext context)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (entityType == ContentEntityTypes.Respawn)
        {
            var monsterIndex = ReadLong(data, "MonsterIndex") ?? 0;
            if (!context.MonsterIndices.Contains(monsterIndex))
            {
                errors["MonsterIndex"] = [$"MonsterIndex references missing monster {monsterIndex}."];
            }

            ValidatePoint(data, "Location", mapFileName, context, errors, "respawn");
        }
        else
        {
            var script = ReadString(data, "FileName");
            if (string.IsNullOrWhiteSpace(script) || !context.ScriptKeys.Contains(NormalizeScriptKey(script)))
            {
                errors["FileName"] = [$"NPC script '{script}' does not exist."];
            }

            ValidatePoint(data, "Location", mapFileName, context, errors, "npc");
        }

        if (errors.Count > 0)
        {
            throw new DraftInputValidationException(errors);
        }
    }

    private static void ValidatePoint(
        JsonElement item,
        string property,
        string mapFileName,
        RelationContext context,
        IDictionary<string, string[]> errors,
        string path)
    {
        var point = property.Length == 0
            ? item
            : item.TryGetProperty(property, out var value) ? value : default;
        var pointPath = property.Length == 0 ? path : $"{path}.{property}";
        if (point.ValueKind != JsonValueKind.Object)
        {
            errors[pointPath] = [$"{pointPath} must be a point object."];
            return;
        }

        var x = ReadLong(point, "x") ?? -1;
        var y = ReadLong(point, "y") ?? -1;
        if (x < 0 || y < 0)
        {
            errors[pointPath] = [$"{pointPath} coordinates must be non-negative."];
            return;
        }

        if (context.MapBounds.TryGetValue(mapFileName + ".map", out var bounds) &&
            bounds.Width > 0 && bounds.Height > 0 &&
            (x >= bounds.Width || y >= bounds.Height))
        {
            errors[pointPath] =
            [
                $"{pointPath} ({x},{y}) is outside map bounds {bounds.Width}x{bounds.Height}."
            ];
        }
    }

    private static IEnumerable<string> ReadScriptKeys(JsonElement data, string contentKey)
    {
        var values = new[]
        {
            ReadString(data, "fileNameKey"),
            ReadString(data, "relativePath"),
            ReadString(data, "fileName"),
            contentKey
        };
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return NormalizeScriptKey(value!);
        }
    }

    private static string NormalizeScriptKey(string value)
    {
        return value.Trim()
            .Replace('\\', '/')
            .Replace(".txt", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/')
            .ToLowerInvariant();
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return value.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static JsonObject ParseObject(JsonElement data)
    {
        return JsonNode.Parse(data.GetRawText()) as JsonObject
               ?? throw new JsonException("Relation data must be a JSON object.");
    }

    private static JsonElement Serialize(JsonNode node)
    {
        return JsonSerializer.SerializeToElement(node, ContentJson.CompactOptions);
    }

    private static string? ReadString(JsonElement data, string property)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadLong(JsonElement data, string property)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private sealed record MapRow(
        long LocalIndex,
        string ContentKey,
        JsonElement Data,
        long Revision,
        string FileName);

    private sealed class RelationContext
    {
        public HashSet<long> MapIndices { get; } = new();
        public HashSet<long> MonsterIndices { get; } = new();
        public HashSet<long> NpcIndices { get; } = new();
        public HashSet<string> ScriptKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<long, string> MapFileNames { get; } = new();
        public Dictionary<string, (long Width, long Height)> MapBounds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
