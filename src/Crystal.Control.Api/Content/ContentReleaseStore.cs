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
        long importRunId,
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

        await using (var imports = new NpgsqlCommand(
                         """
                         SELECT status
                         FROM content_import_run
                         WHERE id = @runId
                         FOR SHARE;
                         """,
                         connection,
                         transaction))
        {
            imports.Parameters.AddWithValue("runId", importRunId);
            var status = await imports.ExecuteScalarAsync(cancellationToken) as string;
            if (status is null)
            {
                throw new ReleaseNotFoundException($"Import run {importRunId} does not exist.");
            }

            if (!string.Equals(status, "imported", StringComparison.Ordinal))
            {
                throw new ReleaseConflictException(
                    $"Import run {importRunId} is '{status}', only imported runs can create releases.");
            }
        }

        long releaseId;
        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO content_release
                    (import_run_id, version, status, schema_version, format_version,
                     content_hash, created_by, created_at, notes)
                VALUES
                    (@runId, @version, 'draft', @schemaVersion, @formatVersion,
                     @contentHash, @createdBy, @createdAt, @notes)
                RETURNING id;
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("runId", importRunId);
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

        await using (var copy = new NpgsqlCommand(
                         """
                         INSERT INTO content_release_entity
                             (release_id, entity_type, local_index, content_key, payload)
                         SELECT @releaseId, entity_type, local_index, content_key, data
                         FROM content_entity
                         WHERE import_run_id = @runId;
                         """,
                         connection,
                         transaction))
        {
            copy.Parameters.AddWithValue("releaseId", releaseId);
            copy.Parameters.AddWithValue("runId", importRunId);
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

    private const string ReleaseColumns =
        """
        r.id, r.import_run_id, r.version, r.status, r.schema_version, r.format_version,
        r.content_hash, r.manifest_object_key, r.created_by, r.created_at, r.published_at, r.notes,
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
        using var countsDocument = JsonDocument.Parse(reader.GetString(12));
        var counts = countsDocument.RootElement.EnumerateObject()
            .Select(property => new ContentTypeCountDto(property.Name, property.Value.GetInt64()))
            .OrderBy(count => count.EntityType, StringComparer.Ordinal)
            .ToArray();

        return new ReleaseDto(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.GetDateTime(9),
            reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            counts);
    }
}
