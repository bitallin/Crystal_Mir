using System.Text.Json;
using Crystal.Content;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Content;

public sealed class ContentReleaseStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ContentReleaseStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ContentEntity>> GetImportEntitiesAsync(
        long importRunId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, local_index, content_key, schema_version, data::text
            FROM content_entity
            WHERE import_run_id = @runId
            ORDER BY entity_type, local_index, content_key;
            """,
            connection);
        command.Parameters.AddWithValue("runId", importRunId);
        return await ReadEntitiesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentEntity>> GetReleaseEntitiesAsync(
        long releaseId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT e.entity_type, e.local_index, e.content_key, r.schema_version, e.payload::text
            FROM content_release_entity e
            JOIN content_release r ON r.id = e.release_id
            WHERE e.release_id = @releaseId
            ORDER BY e.entity_type, e.local_index, e.content_key;
            """,
            connection);
        command.Parameters.AddWithValue("releaseId", releaseId);
        return await ReadEntitiesAsync(command, cancellationToken);
    }

    public async Task<ReleaseDto> CreateAsync(
        long? importRunId,
        long? draftId,
        string version,
        string? notes,
        long? createdBy,
        DateTimeOffset createdAt,
        int schemaVersion,
        int formatVersion,
        string contentHash,
        int expectedEntityCount,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (importRunId is long runId)
        {
            await using var imports = new NpgsqlCommand(
                """
                SELECT status
                FROM content_import_run
                WHERE id = @runId
                FOR SHARE;
                """,
                connection,
                transaction);
            imports.Parameters.AddWithValue("runId", runId);
            var status = await imports.ExecuteScalarAsync(cancellationToken) as string;
            if (status is null)
            {
                throw new ReleaseNotFoundException($"Import run {runId} does not exist.");
            }

            if (!string.Equals(status, "imported", StringComparison.Ordinal))
            {
                throw new ReleaseConflictException(
                    $"Import run {runId} is '{status}', only imported runs can create releases.");
            }
        }

        if (draftId is long sourceDraftId)
        {
            await using var draft = new NpgsqlCommand(
                """
                SELECT status
                FROM content_draft
                WHERE id = @draftId
                FOR SHARE;
                """,
                connection,
                transaction);
            draft.Parameters.AddWithValue("draftId", sourceDraftId);
            var status = await draft.ExecuteScalarAsync(cancellationToken) as string;
            if (status is null)
            {
                throw new ReleaseNotFoundException($"Draft {sourceDraftId} does not exist.");
            }

            if (status == "archived")
            {
                throw new ReleaseConflictException($"Draft {sourceDraftId} is archived.");
            }
        }

        long releaseId;
        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO content_release
                    (import_run_id, draft_id, version, status, schema_version, format_version,
                     content_hash, created_by, created_at, notes)
                VALUES
                    (@runId, @draftId, @version, 'draft', @schemaVersion, @formatVersion,
                     @contentHash, @createdBy, @createdAt, @notes)
                RETURNING id;
                """,
                connection,
                transaction);
            insert.Parameters.Add(new NpgsqlParameter("runId", NpgsqlDbType.Bigint)
            {
                Value = importRunId is null ? DBNull.Value : importRunId.Value
            });
            insert.Parameters.Add(new NpgsqlParameter("draftId", NpgsqlDbType.Bigint)
            {
                Value = draftId is null ? DBNull.Value : draftId.Value
            });
            insert.Parameters.AddWithValue("version", version);
            insert.Parameters.AddWithValue("schemaVersion", schemaVersion);
            insert.Parameters.AddWithValue("formatVersion", formatVersion);
            insert.Parameters.AddWithValue("contentHash", contentHash);
            insert.Parameters.Add(new NpgsqlParameter("createdBy", NpgsqlDbType.Bigint)
            {
                Value = createdBy is null ? DBNull.Value : createdBy.Value
            });
            insert.Parameters.AddWithValue("createdAt", createdAt.UtcDateTime);
            insert.Parameters.Add(new NpgsqlParameter("notes", NpgsqlDbType.Text)
            {
                Value = (object?)notes ?? DBNull.Value
            });
            releaseId = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ReleaseConflictException($"Release version '{version}' already exists.");
        }

        string copySql = draftId is null
            ? """
              INSERT INTO content_release_entity
                  (release_id, entity_type, local_index, content_key, payload)
              SELECT @releaseId, entity_type, local_index, content_key, data
              FROM content_entity
              WHERE import_run_id = @runId;
              """
            : """
              INSERT INTO content_release_entity
                  (release_id, entity_type, local_index, content_key, payload)
              SELECT @releaseId, entity_type, local_index, content_key, data
              FROM content_draft_entity
              WHERE draft_id = @draftId;
              """;

        await using (var copy = new NpgsqlCommand(copySql, connection, transaction))
        {
            copy.Parameters.AddWithValue("releaseId", releaseId);
            copy.Parameters.Add(new NpgsqlParameter("runId", NpgsqlDbType.Bigint)
            {
                Value = importRunId is null ? DBNull.Value : importRunId.Value
            });
            copy.Parameters.Add(new NpgsqlParameter("draftId", NpgsqlDbType.Bigint)
            {
                Value = draftId is null ? DBNull.Value : draftId.Value
            });
            await copy.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var count = new NpgsqlCommand(
                         """
                         SELECT count(*)::integer
                         FROM content_release_entity
                         WHERE release_id = @releaseId;
                         """,
                         connection,
                         transaction))
        {
            count.Parameters.AddWithValue("releaseId", releaseId);
            var actual = (int)(await count.ExecuteScalarAsync(cancellationToken))!;
            if (actual != expectedEntityCount)
            {
                throw new ReleaseConflictException(
                    $"Release entity count mismatch: expected {expectedEntityCount}, copied {actual}.");
            }
        }

        IReadOnlyList<ContentEntity> copiedEntities;
        await using (var copied = new NpgsqlCommand(
                         """
                         SELECT e.entity_type, e.local_index, e.content_key, r.schema_version, e.payload::text
                         FROM content_release_entity e
                         JOIN content_release r ON r.id = e.release_id
                         WHERE e.release_id = @releaseId
                         ORDER BY e.entity_type, e.local_index, e.content_key;
                         """,
                         connection,
                         transaction))
        {
            copied.Parameters.AddWithValue("releaseId", releaseId);
            copiedEntities = await ReadEntitiesAsync(copied, cancellationToken);
        }

        string copiedContentHash;
        try
        {
            copiedContentHash = ContentSnapshotWriter.Build(new ContentSnapshotWriteOptions
            {
                OutputDirectory = string.Empty,
                ReleaseVersion = version,
                Source = "release-copy-verification"
            }, copiedEntities).Manifest.ContentHash;
        }
        catch (InvalidDataException exception)
        {
            throw new ReleaseConflictException(
                $"Source content changed while creating release '{version}': {exception.Message}");
        }

        if (!string.Equals(copiedContentHash, contentHash, StringComparison.Ordinal))
        {
            throw new ReleaseConflictException(
                $"Source content changed while creating release '{version}'; retry the operation.");
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(releaseId, cancellationToken)
               ?? throw new ReleaseNotFoundException($"Release {releaseId} was not found after creation.");
    }

    public async Task<ReleaseDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {ReleaseColumns}
             FROM content_release r
             WHERE r.id = @id;
             """,
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRelease(reader) : null;
    }

    public async Task<IReadOnlyList<ReleaseDto>> ListAsync(
        int limit,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {ReleaseColumns}
             FROM content_release r
             WHERE (@status IS NULL OR r.status = @status)
             ORDER BY r.id DESC
             LIMIT @limit;
             """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text)
        {
            Value = (object?)status ?? DBNull.Value
        });
        command.Parameters.AddWithValue("limit", limit);

        var releases = new List<ReleaseDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            releases.Add(ReadRelease(reader));
        }

        return releases;
    }

    public async Task<ReleaseDto?> TransitionAsync(
        long id,
        IReadOnlyList<string> allowedStatuses,
        string nextStatus,
        string? contentHash,
        string? manifestObjectKey,
        bool published,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE content_release
            SET status = @nextStatus,
                content_hash = COALESCE(@contentHash, content_hash),
                manifest_object_key = COALESCE(@manifestObjectKey, manifest_object_key),
                published_at = CASE WHEN @published THEN now() ELSE published_at END
            WHERE id = @id
              AND status = ANY(@allowedStatuses)
            RETURNING id;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.Add(new NpgsqlParameter("allowedStatuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = allowedStatuses.ToArray()
        });
        command.Parameters.AddWithValue("nextStatus", nextStatus);
        command.Parameters.Add(new NpgsqlParameter("contentHash", NpgsqlDbType.Text)
        {
            Value = (object?)contentHash ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("manifestObjectKey", NpgsqlDbType.Text)
        {
            Value = (object?)manifestObjectKey ?? DBNull.Value
        });
        command.Parameters.AddWithValue("published", published);

        var updated = await command.ExecuteScalarAsync(cancellationToken);
        return updated is long updatedId ? await GetAsync(updatedId, cancellationToken) : null;
    }

    public async Task<ReleaseRollbackDto> RollbackAsync(
        long id,
        long? targetReleaseId,
        string reason,
        long? actorAccountId,
        DateTimeOffset rolledBackAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string currentStatus;
        await using (var current = new NpgsqlCommand(
                         "SELECT status FROM content_release WHERE id = @id FOR UPDATE;",
                         connection,
                         transaction))
        {
            current.Parameters.AddWithValue("id", id);
            currentStatus = await current.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new ReleaseNotFoundException($"Release {id} does not exist.");
        }

        if (currentStatus is not ("published" or "active"))
        {
            throw new ReleaseConflictException(
                $"Release {id} is '{currentStatus}', only published or active releases can be rolled back.");
        }

        long? activeId = null;
        await using (var active = new NpgsqlCommand(
                         "SELECT id FROM content_release WHERE status = 'active' FOR UPDATE;",
                         connection,
                         transaction))
        {
            if (await active.ExecuteScalarAsync(cancellationToken) is long activeReleaseId)
            {
                activeId = activeReleaseId;
            }
        }

        if (activeId is long otherActive && otherActive != id)
        {
            throw new ReleaseConflictException(
                $"Release {otherActive} is active; only the active release or a published release without an active target can be rolled back.");
        }

        long resolvedTargetId;
        string targetStatus;
        if (targetReleaseId is long requestedTargetId)
        {
            resolvedTargetId = requestedTargetId;
            await using var target = new NpgsqlCommand(
                "SELECT status FROM content_release WHERE id = @id FOR UPDATE;",
                connection,
                transaction);
            target.Parameters.AddWithValue("id", resolvedTargetId);
            targetStatus = await target.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new ReleaseNotFoundException($"Target release {resolvedTargetId} does not exist.");
        }
        else
        {
            await using var target = new NpgsqlCommand(
                """
                SELECT id, status
                FROM content_release
                WHERE id <> @id AND status = 'published'
                ORDER BY published_at DESC NULLS LAST, id DESC
                LIMIT 1
                FOR UPDATE;
                """,
                connection,
                transaction);
            target.Parameters.AddWithValue("id", id);
            await using var reader = await target.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ReleaseConflictException("No previous published release is available to roll back to.");
            }

            resolvedTargetId = reader.GetInt64(0);
            targetStatus = reader.GetString(1);
        }

        if (resolvedTargetId == id)
        {
            throw new ReleaseConflictException("targetReleaseId must refer to a different release.");
        }

        if (!string.Equals(targetStatus, "published", StringComparison.Ordinal))
        {
            throw new ReleaseConflictException(
                $"Target release {resolvedTargetId} is '{targetStatus}', expected 'published'.");
        }

        await using (var rollback = new NpgsqlCommand(
                         """
                         UPDATE content_release
                         SET status = 'rolled_back',
                             rolled_back_at = @rolledBackAt,
                             rolled_back_by = @actor,
                             rollback_reason = @reason,
                             rollback_target_id = @targetId
                         WHERE id = @id;
                         """,
                         connection,
                         transaction))
        {
            rollback.Parameters.AddWithValue("rolledBackAt", rolledBackAt.UtcDateTime);
            rollback.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Bigint)
            {
                Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
            });
            rollback.Parameters.AddWithValue("reason", reason);
            rollback.Parameters.AddWithValue("targetId", resolvedTargetId);
            rollback.Parameters.AddWithValue("id", id);
            await rollback.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var activate = new NpgsqlCommand(
                         """
                         UPDATE content_release
                         SET status = 'active',
                             rolled_back_at = NULL,
                             rolled_back_by = NULL,
                             rollback_reason = NULL,
                             rollback_target_id = NULL
                         WHERE id = @targetId AND status = 'published';
                         """,
                         connection,
                         transaction))
        {
            activate.Parameters.AddWithValue("targetId", resolvedTargetId);
            if (await activate.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new ReleaseConflictException(
                    $"Target release {resolvedTargetId} changed while rolling back.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var rolledBack = await GetAsync(id, cancellationToken)
                         ?? throw new ReleaseNotFoundException($"Release {id} disappeared after rollback.");
        var activeRelease = await GetAsync(resolvedTargetId, cancellationToken)
                            ?? throw new ReleaseNotFoundException(
                                $"Target release {resolvedTargetId} disappeared after rollback.");
        return new ReleaseRollbackDto(rolledBack, activeRelease, reason, rolledBackAt.UtcDateTime);
    }

    private const string ReleaseColumns =
        """
        r.id, r.import_run_id, r.draft_id, r.version, r.status, r.schema_version, r.format_version,
        r.content_hash, r.manifest_object_key, r.created_by, r.created_at, r.published_at, r.notes,
        r.rolled_back_at, r.rollback_reason, r.rollback_target_id,
        COALESCE((
            SELECT jsonb_object_agg(counts.entity_type, counts.count)
            FROM (
                SELECT entity_type, count(*)::bigint AS count
                FROM content_release_entity
                WHERE release_id = r.id
                GROUP BY entity_type
            ) counts
        ), '{}'::jsonb)::text
        """;

    private static async Task<IReadOnlyList<ContentEntity>> ReadEntitiesAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
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

    private static ReleaseDto ReadRelease(NpgsqlDataReader reader)
    {
        using var countsDocument = JsonDocument.Parse(reader.GetString(16));
        var counts = countsDocument.RootElement.EnumerateObject()
            .Select(property => new ContentTypeCountDto(property.Name, property.Value.GetInt64()))
            .OrderBy(count => count.EntityType, StringComparer.Ordinal)
            .ToArray();

        return new ReleaseDto(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.GetDateTime(10),
            reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetInt64(15),
            counts);
    }
}
