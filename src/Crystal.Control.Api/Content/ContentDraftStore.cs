using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Content;

public sealed class ContentDraftStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ContentDraftStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<DraftDto> CreateAsync(
        long importRunId,
        string name,
        long? createdBy,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var import = new NpgsqlCommand(
                         """
                         SELECT status
                         FROM content_import_run
                         WHERE id = @runId
                         FOR SHARE;
                         """,
                         connection,
                         transaction))
        {
            import.Parameters.AddWithValue("runId", importRunId);
            var status = await import.ExecuteScalarAsync(cancellationToken) as string;
            if (status is null)
            {
                throw new DraftNotFoundException($"Import run {importRunId} does not exist.");
            }

            if (!string.Equals(status, "imported", StringComparison.Ordinal))
            {
                throw new DraftConflictException(
                    $"Import run {importRunId} is '{status}', only imported runs can create drafts.");
            }
        }

        long draftId;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO content_draft
                             (name, base_import_run_id, created_by, updated_by, created_at, updated_at)
                         VALUES
                             (@name, @runId, @actor, @actor, @createdAt, @createdAt)
                         RETURNING id;
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("name", name);
            insert.Parameters.AddWithValue("runId", importRunId);
            insert.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
            {
                Value = createdBy is null ? DBNull.Value : createdBy.Value
            });
            insert.Parameters.AddWithValue("createdAt", createdAt.UtcDateTime);
            draftId = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        await using (var copy = new NpgsqlCommand(
                         """
                         INSERT INTO content_draft_entity
                             (draft_id, entity_type, local_index, content_key, schema_version, data, revision)
                         SELECT @draftId, entity_type, local_index, content_key, schema_version, data, 1
                         FROM content_entity
                         WHERE import_run_id = @runId;
                         """,
                         connection,
                         transaction))
        {
            copy.Parameters.AddWithValue("draftId", draftId);
            copy.Parameters.AddWithValue("runId", importRunId);
            await copy.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var count = new NpgsqlCommand(
                         """
                         UPDATE content_draft
                         SET entity_count = (
                             SELECT count(*)::integer
                             FROM content_draft_entity
                             WHERE draft_id = @draftId
                         )
                         WHERE id = @draftId;
                         """,
                         connection,
                         transaction))
        {
            count.Parameters.AddWithValue("draftId", draftId);
            await count.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(draftId, cancellationToken)
               ?? throw new DraftNotFoundException($"Draft {draftId} was not found after creation.");
    }

    public async Task<DraftDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {DraftColumns}
             FROM content_draft d
             WHERE d.id = @id;
             """,
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDraft(reader) : null;
    }

    public async Task<IReadOnlyList<DraftDto>> ListAsync(
        int limit,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {DraftColumns}
             FROM content_draft d
             WHERE (@status IS NULL OR d.status = @status)
             ORDER BY d.updated_at DESC, d.id DESC
             LIMIT @limit;
             """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text)
        {
            Value = (object?)status ?? DBNull.Value
        });
        command.Parameters.AddWithValue("limit", limit);

        var drafts = new List<DraftDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            drafts.Add(ReadDraft(reader));
        }

        return drafts;
    }

    public async Task<IReadOnlyList<ContentEntity>> GetEntitiesAsync(
        long draftId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, local_index, content_key, schema_version, data::text
            FROM content_draft_entity
            WHERE draft_id = @draftId
            ORDER BY entity_type, local_index, content_key;
            """,
            connection);
        command.Parameters.AddWithValue("draftId", draftId);

        var entities = new List<ContentEntity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var document = JsonDocument.Parse(reader.GetString(4));
            entities.Add(new ContentEntity
            {
                EntityType = reader.GetString(0),
                LocalIndex = reader.GetInt64(1),
                ContentKey = reader.GetString(2),
                SchemaVersion = reader.GetInt32(3),
                Data = document.RootElement.Clone()
            });
        }

        return entities;
    }

    public async Task<(IReadOnlyList<DraftEntityDto> Items, long Total)> QueryEntitiesAsync(
        long draftId,
        string entityType,
        int page,
        int pageSize,
        string? query,
        string sort,
        string direction,
        CancellationToken cancellationToken)
    {
        var orderExpression = sort switch
        {
            "key" => "e.content_key",
            "name" => "lower(coalesce(e.data->>'Name', e.data->>'Title', e.data->>'FileName', e.content_key)) COLLATE \"C\"",
            _ => "e.local_index"
        };
        var orderDirection = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";
        var where = "e.draft_id = @draftId AND e.entity_type = @entityType";
        if (!string.IsNullOrWhiteSpace(query))
        {
            where += """
                 AND lower(coalesce(e.data->>'Name', e.data->>'Title', e.data->>'FileName', e.content_key))
                     LIKE lower(@pattern) ESCAPE E'\\'
                """;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var count = new NpgsqlCommand(
            $"SELECT count(*)::bigint FROM content_draft_entity e WHERE {where};",
            connection);
        count.Parameters.AddWithValue("draftId", draftId);
        count.Parameters.AddWithValue("entityType", entityType);
        if (!string.IsNullOrWhiteSpace(query))
        {
            count.Parameters.AddWithValue("pattern", $"%{EscapeLike(query.Trim())}%");
        }

        var total = (long)(await count.ExecuteScalarAsync(cancellationToken))!;

        await using var command = new NpgsqlCommand(
            $"""
             SELECT e.draft_id, e.entity_type, e.local_index, e.content_key,
                    e.schema_version, e.data::text, e.revision, e.updated_by, e.updated_at
             FROM content_draft_entity e
             WHERE {where}
             ORDER BY {orderExpression} {orderDirection}, e.local_index ASC
             LIMIT @limit OFFSET @offset;
             """,
            connection);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);
        if (!string.IsNullOrWhiteSpace(query))
        {
            command.Parameters.AddWithValue("pattern", $"%{EscapeLike(query.Trim())}%");
        }

        var items = new List<DraftEntityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadEntity(reader));
        }

        return (items, total);
    }

    public async Task<DraftEntityDto?> GetEntityAsync(
        long draftId,
        string entityType,
        long localIndex,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT draft_id, entity_type, local_index, content_key,
                   schema_version, data::text, revision, updated_by, updated_at
            FROM content_draft_entity
            WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex;
            """,
            connection);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("localIndex", localIndex);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
    }

    public async Task<DraftEntityDto> CreateEntityAsync(
        long draftId,
        string entityType,
        JsonElement data,
        int schemaVersion,
        long? actorAccountId,
        string? reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string draftStatus;
        await using (var draft = new NpgsqlCommand(
                         "SELECT status FROM content_draft WHERE id = @draftId FOR UPDATE;",
                         connection,
                         transaction))
        {
            draft.Parameters.AddWithValue("draftId", draftId);
            draftStatus = await draft.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new DraftNotFoundException($"Draft {draftId} does not exist.");
        }

        if (draftStatus == "archived")
        {
            throw new DraftConflictException($"Draft {draftId} is archived and cannot be edited.");
        }

        long localIndex;
        await using (var nextIndex = new NpgsqlCommand(
                         """
                         SELECT COALESCE(max(local_index), 0) + 1
                         FROM content_draft_entity
                         WHERE draft_id = @draftId AND entity_type = @entityType;
                         """,
                         connection,
                         transaction))
        {
            nextIndex.Parameters.AddWithValue("draftId", draftId);
            nextIndex.Parameters.AddWithValue("entityType", entityType);
            localIndex = (long)(await nextIndex.ExecuteScalarAsync(cancellationToken))!;
        }

        if (localIndex > int.MaxValue)
        {
            throw new DraftConflictException(
                $"No item local index is available in draft {draftId}.");
        }

        JsonObject payload;
        try
        {
            payload = JsonNode.Parse(data.GetRawText())?.AsObject()
                      ?? throw new JsonException("Item data must be a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new DraftInputValidationException(new Dictionary<string, string[]>
            {
                ["data"] = [exception.Message]
            });
        }

        payload["Index"] = localIndex;
        var normalizedData = JsonSerializer.SerializeToElement(payload, ContentJson.CompactOptions);
        var contentKey = $"{entityType}.{localIndex}";
        var validationErrors = ContentItemEditorSchema.ValidateStructure(normalizedData, localIndex, contentKey);
        if (validationErrors.Count > 0)
        {
            throw new DraftInputValidationException(validationErrors);
        }

        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO content_draft_entity
                    (draft_id, entity_type, local_index, content_key, schema_version, data,
                     revision, updated_by, updated_at)
                VALUES
                    (@draftId, @entityType, @localIndex, @contentKey, @schemaVersion,
                     @data, 1, @actor, @occurredAt);
                """,
                connection,
                transaction);
            AddEntityParameters(insert, draftId, entityType, localIndex, contentKey, schemaVersion,
                normalizedData, 1, actorAccountId, occurredAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DraftConflictException(
                $"Entity type/index/key already exists in draft {draftId}: {entityType}/{localIndex}/{contentKey}.");
        }

        await InsertRevisionAsync(connection, transaction, draftId, "create", entityType, localIndex,
            contentKey, null, normalizedData, actorAccountId, reason, occurredAt, cancellationToken);
        await TouchDraftAsync(connection, transaction, draftId, actorAccountId, occurredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetEntityAsync(draftId, entityType, localIndex, cancellationToken)
               ?? throw new DraftNotFoundException($"Draft entity {entityType}/{localIndex} was not found after creation.");
    }

    public async Task<DraftEntityDto> UpsertEntityAsync(
        long draftId,
        string entityType,
        long localIndex,
        UpsertDraftEntityRequest request,
        JsonElement data,
        string? contentKey,
        int schemaVersion,
        long? actorAccountId,
        string? reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string draftStatus;
        await using (var draft = new NpgsqlCommand(
                         "SELECT status FROM content_draft WHERE id = @draftId FOR UPDATE;",
                         connection,
                         transaction))
        {
            draft.Parameters.AddWithValue("draftId", draftId);
            draftStatus = await draft.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new DraftNotFoundException($"Draft {draftId} does not exist.");
        }

        if (draftStatus == "archived")
        {
            throw new DraftConflictException($"Draft {draftId} is archived and cannot be edited.");
        }

        long? currentRevision = null;
        string? currentContentKey = null;
        string? beforeJson = null;
        await using (var existing = new NpgsqlCommand(
                         """
                         SELECT content_key, revision, data::text
                         FROM content_draft_entity
                         WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("draftId", draftId);
            existing.Parameters.AddWithValue("entityType", entityType);
            existing.Parameters.AddWithValue("localIndex", localIndex);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                currentContentKey = reader.GetString(0);
                currentRevision = reader.GetInt64(1);
                beforeJson = reader.GetString(2);
            }
        }

        if (currentRevision is null)
        {
            if (request.ExpectedRevision != 0)
            {
                throw new DraftConflictException(
                    $"Entity {entityType}/{localIndex} has no revision; expectedRevision must be 0.");
            }

            if (string.IsNullOrWhiteSpace(contentKey))
            {
                throw new DraftConflictException("contentKey is required when creating an entity.");
            }

            long revision = 1;
            try
            {
                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO content_draft_entity
                        (draft_id, entity_type, local_index, content_key, schema_version, data,
                         revision, updated_by, updated_at)
                    VALUES
                        (@draftId, @entityType, @localIndex, @contentKey, @schemaVersion,
                         @data, @revision, @actor, @occurredAt);
                    """,
                    connection,
                    transaction);
                AddEntityParameters(insert, draftId, entityType, localIndex, contentKey!, schemaVersion,
                    data, revision, actorAccountId, occurredAt);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new DraftConflictException(
                    $"Entity type/index/key already exists in draft {draftId}: {entityType}/{localIndex}/{contentKey}.");
            }

            await InsertRevisionAsync(connection, transaction, draftId, "create", entityType, localIndex,
                contentKey!, null, data, actorAccountId, reason, occurredAt, cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(contentKey) &&
                !string.Equals(contentKey, currentContentKey, StringComparison.Ordinal))
            {
                throw new DraftConflictException("contentKey is immutable; create a new entity instead.");
            }

            if (request.ExpectedRevision != currentRevision.Value)
            {
                throw new DraftConflictException(
                    $"Entity {entityType}/{localIndex} has revision {currentRevision}; expected {request.ExpectedRevision}.");
            }

            long revision = currentRevision.Value + 1;
            await using var update = new NpgsqlCommand(
                """
                UPDATE content_draft_entity
                SET data = @data,
                    schema_version = @schemaVersion,
                    revision = @revision,
                    updated_by = @actor,
                    updated_at = @occurredAt
                WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex;
                """,
                connection,
                transaction);
            update.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = data.GetRawText() });
            update.Parameters.AddWithValue("schemaVersion", schemaVersion);
            update.Parameters.AddWithValue("revision", revision);
            update.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
            {
                Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
            });
            update.Parameters.AddWithValue("occurredAt", occurredAt.UtcDateTime);
            update.Parameters.AddWithValue("draftId", draftId);
            update.Parameters.AddWithValue("entityType", entityType);
            update.Parameters.AddWithValue("localIndex", localIndex);
            await update.ExecuteNonQueryAsync(cancellationToken);

            using var before = JsonDocument.Parse(beforeJson!);
            await InsertRevisionAsync(connection, transaction, draftId, "update", entityType, localIndex,
                currentContentKey!, before.RootElement.Clone(), data, actorAccountId, reason, occurredAt,
                cancellationToken);
        }

        await TouchDraftAsync(connection, transaction, draftId, actorAccountId, occurredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetEntityAsync(draftId, entityType, localIndex, cancellationToken)
               ?? throw new DraftNotFoundException($"Draft entity {entityType}/{localIndex} was not found after save.");
    }

    public async Task DeleteEntityAsync(
        long draftId,
        string entityType,
        long localIndex,
        long expectedRevision,
        string? reason,
        long? actorAccountId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string draftStatus;
        await using (var draft = new NpgsqlCommand(
                         "SELECT status FROM content_draft WHERE id = @draftId FOR UPDATE;",
                         connection,
                         transaction))
        {
            draft.Parameters.AddWithValue("draftId", draftId);
            draftStatus = await draft.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new DraftNotFoundException($"Draft {draftId} does not exist.");
        }

        if (draftStatus == "archived")
        {
            throw new DraftConflictException($"Draft {draftId} is archived and cannot be edited.");
        }

        string contentKey;
        long revision;
        string beforeJson;
        await using (var existing = new NpgsqlCommand(
                         """
                         SELECT content_key, revision, data::text
                         FROM content_draft_entity
                         WHERE draft_id = @draftId AND entity_type = @entityType AND local_index = @localIndex
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("draftId", draftId);
            existing.Parameters.AddWithValue("entityType", entityType);
            existing.Parameters.AddWithValue("localIndex", localIndex);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new DraftNotFoundException(
                    $"Draft entity {entityType}/{localIndex} does not exist in draft {draftId}.");
            }

            contentKey = reader.GetString(0);
            revision = reader.GetInt64(1);
            beforeJson = reader.GetString(2);
        }

        if (revision != expectedRevision)
        {
            throw new DraftConflictException(
                $"Entity {entityType}/{localIndex} has revision {revision}; expected {expectedRevision}.");
        }

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

        using var before = JsonDocument.Parse(beforeJson);
        await InsertRevisionAsync(connection, transaction, draftId, "delete", entityType, localIndex,
            contentKey, before.RootElement.Clone(), null, actorAccountId, reason, occurredAt,
            cancellationToken);
        await TouchDraftAsync(connection, transaction, draftId, actorAccountId, occurredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveValidationAsync(
        long draftId,
        DraftValidationReportDto report,
        DateTimeOffset validatedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE content_draft
            SET status = @status,
                last_validation = @validation,
                updated_at = @validatedAt
            WHERE id = @draftId;
            """,
            connection);
        command.Parameters.AddWithValue("status", report.Valid ? "validated" : "failed");
        command.Parameters.Add(new NpgsqlParameter("validation", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(report, ContentJson.CompactOptions)
        });
        command.Parameters.AddWithValue("validatedAt", validatedAt.UtcDateTime);
        command.Parameters.AddWithValue("draftId", draftId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DraftRevisionDto>> ListRevisionsAsync(
        long draftId,
        string? entityType,
        string? contentKey,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT revision_number, action, entity_type, local_index, content_key,
                   before_data::text, after_data::text, actor_account_id, reason, occurred_at
            FROM content_draft_revision
            WHERE draft_id = @draftId
              AND (@entityType IS NULL OR entity_type = @entityType)
              AND (@contentKey IS NULL OR content_key = @contentKey)
            ORDER BY revision_number DESC
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.Add(new NpgsqlParameter("entityType", NpgsqlDbType.Text)
        {
            Value = (object?)entityType ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("contentKey", NpgsqlDbType.Text)
        {
            Value = (object?)contentKey ?? DBNull.Value
        });
        command.Parameters.AddWithValue("limit", limit);

        var revisions = new List<DraftRevisionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(new DraftRevisionDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                ReadOptionalJson(reader, 5),
                ReadOptionalJson(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetDateTime(9)));
        }

        return revisions;
    }

    private static async Task InsertRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        string action,
        string entityType,
        long localIndex,
        string contentKey,
        JsonElement? beforeData,
        JsonElement? afterData,
        long? actorAccountId,
        string? reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var sequence = new NpgsqlCommand(
            """
            SELECT COALESCE(max(revision_number), 0) + 1
            FROM content_draft_revision
            WHERE draft_id = @draftId;
            """,
            connection,
            transaction);
        sequence.Parameters.AddWithValue("draftId", draftId);
        var revisionNumber = (long)(await sequence.ExecuteScalarAsync(cancellationToken))!;

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO content_draft_revision
                (draft_id, revision_number, action, entity_type, local_index, content_key,
                 before_data, after_data, actor_account_id, reason, occurred_at)
            VALUES
                (@draftId, @revisionNumber, @action, @entityType, @localIndex, @contentKey,
                 @beforeData, @afterData, @actor, @reason, @occurredAt);
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("draftId", draftId);
        insert.Parameters.AddWithValue("revisionNumber", revisionNumber);
        insert.Parameters.AddWithValue("action", action);
        insert.Parameters.AddWithValue("entityType", entityType);
        insert.Parameters.AddWithValue("localIndex", localIndex);
        insert.Parameters.AddWithValue("contentKey", contentKey);
        insert.Parameters.Add(new NpgsqlParameter("beforeData", NpgsqlDbType.Jsonb)
        {
            Value = beforeData is null ? DBNull.Value : beforeData.Value.GetRawText()
        });
        insert.Parameters.Add(new NpgsqlParameter("afterData", NpgsqlDbType.Jsonb)
        {
            Value = afterData is null ? DBNull.Value : afterData.Value.GetRawText()
        });
        insert.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
        {
            Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
        });
        insert.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Text)
        {
            Value = (object?)reason ?? DBNull.Value
        });
        insert.Parameters.AddWithValue("occurredAt", occurredAt.UtcDateTime);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TouchDraftAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long draftId,
        long? actorAccountId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE content_draft
            SET status = 'editing',
                last_validation = NULL,
                updated_by = @actor,
                updated_at = @occurredAt,
                entity_count = (
                    SELECT count(*)::integer
                    FROM content_draft_entity
                    WHERE draft_id = @draftId
                )
            WHERE id = @draftId;
            """,
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
        {
            Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
        });
        command.Parameters.AddWithValue("occurredAt", occurredAt.UtcDateTime);
        command.Parameters.AddWithValue("draftId", draftId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEntityParameters(
        NpgsqlCommand command,
        long draftId,
        string entityType,
        long localIndex,
        string contentKey,
        int schemaVersion,
        JsonElement data,
        long revision,
        long? actorAccountId,
        DateTimeOffset occurredAt)
    {
        command.Parameters.AddWithValue("draftId", draftId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("localIndex", localIndex);
        command.Parameters.AddWithValue("contentKey", contentKey);
        command.Parameters.AddWithValue("schemaVersion", schemaVersion);
        command.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = data.GetRawText() });
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
        {
            Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
        });
        command.Parameters.AddWithValue("occurredAt", occurredAt.UtcDateTime);
    }

    private static DraftDto ReadDraft(NpgsqlDataReader reader)
    {
        using var countsDocument = JsonDocument.Parse(reader.GetString(10));
        var counts = countsDocument.RootElement.EnumerateObject()
            .Select(property => new ContentTypeCountDto(property.Name, property.Value.GetInt64()))
            .OrderBy(count => count.EntityType, StringComparer.Ordinal)
            .ToArray();

        JsonElement? validation = null;
        if (!reader.IsDBNull(9))
        {
            using var document = JsonDocument.Parse(reader.GetString(9));
            validation = document.RootElement.Clone();
        }

        return new DraftDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.GetDateTime(6),
            reader.GetDateTime(7),
            reader.GetInt32(8),
            validation,
            counts);
    }

    private static DraftEntityDto ReadEntity(NpgsqlDataReader reader)
    {
        using var document = JsonDocument.Parse(reader.GetString(5));
        return new DraftEntityDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetInt32(4),
            document.RootElement.Clone(),
            reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.GetDateTime(8));
    }

    private static JsonElement? ReadOptionalJson(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        using var document = JsonDocument.Parse(reader.GetString(ordinal));
        return document.RootElement.Clone();
    }

    private static string EscapeLike(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private const string DraftColumns =
        """
        d.id, d.name, d.base_import_run_id, d.status, d.created_by, d.updated_by,
        d.created_at, d.updated_at, d.entity_count, d.last_validation::text,
        COALESCE((
            SELECT jsonb_object_agg(counts.entity_type, counts.count)
            FROM (
                SELECT entity_type, count(*)::bigint AS count
                FROM content_draft_entity
                WHERE draft_id = d.id
                GROUP BY entity_type
            ) counts
        ), '{}'::jsonb)::text
        """;
}
