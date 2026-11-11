using System.Text.Json;
using Crystal.Content;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Content;

public sealed class ContentQueryStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ContentQueryStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<bool> IsDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT to_regclass('public.content_entity') IS NOT NULL
               AND to_regclass('public.control_account') IS NOT NULL
               AND to_regclass('public.control_session') IS NOT NULL
               AND to_regclass('public.audit_event') IS NOT NULL;
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<ImportRunDto>> GetImportRunsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, source, source_hash, legacy_version, schema_version, status,
                   entity_count, started_at, finished_at
            FROM content_import_run
            ORDER BY id DESC
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("limit", limit);

        var runs = new List<ImportRunDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(ReadImportRun(reader));
        }

        return runs;
    }

    public async Task<ImportRunDto?> GetImportRunAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, source, source_hash, legacy_version, schema_version, status,
                   entity_count, started_at, finished_at
            FROM content_import_run
            WHERE id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadImportRun(reader) : null;
    }

    public async Task<long?> ResolveImportRunIdAsync(long? requestedRunId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM content_import_run
            WHERE status = 'imported'
              AND (@id IS NULL OR id = @id)
            ORDER BY id DESC
            LIMIT 1;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint)
        {
            Value = requestedRunId is null ? DBNull.Value : requestedRunId.Value
        });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long id ? id : null;
    }

    public async Task<IReadOnlyList<ContentTypeCountDto>> GetContentTypeCountsAsync(
        long importRunId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, count(*)::bigint
            FROM content_entity
            WHERE import_run_id = @runId
            GROUP BY entity_type
            ORDER BY entity_type;
            """,
            connection);
        command.Parameters.AddWithValue("runId", importRunId);

        var counts = new List<ContentTypeCountDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new ContentTypeCountDto(reader.GetString(0), reader.GetInt64(1)));
        }

        return counts;
    }

    public async Task<(IReadOnlyList<ContentEntityDto> Items, long Total)> QueryContentAsync(
        long importRunId,
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
            "key" => "content_key",
            "name" => "lower(coalesce(data->>'Name', data->>'Title', data->>'FileName', content_key)) COLLATE \"C\"",
            _ => "local_index"
        };
        var orderDirection = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";

        var where = "import_run_id = @runId AND entity_type = @entityType";
        if (!string.IsNullOrWhiteSpace(query))
        {
            where += """
                 AND lower(coalesce(data->>'Name', data->>'Title', data->>'FileName', content_key))
                     LIKE lower(@pattern) ESCAPE E'\\'
                """;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var total = await CountAsync(connection, where, importRunId, entityType, query, cancellationToken);

        await using var command = new NpgsqlCommand(
            $"""
             SELECT id, import_run_id, content_key, local_index, schema_version, data::text
             FROM content_entity
             WHERE {where}
             ORDER BY {orderExpression} {orderDirection}, local_index ASC
             LIMIT @limit OFFSET @offset;
             """,
            connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);
        AddQueryParameter(command, query);

        var items = new List<ContentEntityDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadEntity(reader));
        }

        return (items, total);
    }

    public async Task<(IReadOnlyList<MapSummaryDto> Items, long Total)> QueryMapCatalogAsync(
        long importRunId,
        int page,
        int pageSize,
        string? query,
        CancellationToken cancellationToken)
    {
        var where = "import_run_id = @runId AND entity_type = @entityType";
        if (!string.IsNullOrWhiteSpace(query))
        {
            where += """
                 AND (
                     lower(content_key) LIKE lower(@pattern) ESCAPE E'\\'
                     OR lower(coalesce(data->>'Title', '')) LIKE lower(@pattern) ESCAPE E'\\'
                     OR lower(coalesce(data->>'FileName', '')) LIKE lower(@pattern) ESCAPE E'\\'
                     OR local_index::text = @exactQuery
                 )
                """;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var total = await CountAsync(
            connection,
            where,
            importRunId,
            ContentEntityTypes.Map,
            query,
            cancellationToken,
            addExactQuery: true);

        await using var command = new NpgsqlCommand(
            $"""
             SELECT id, import_run_id, content_key, local_index, schema_version, data::text
             FROM content_entity
             WHERE {where}
             ORDER BY local_index ASC
             LIMIT @limit OFFSET @offset;
             """,
            connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", ContentEntityTypes.Map);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);
        AddQueryParameter(command, query, addExactQuery: true);

        var items = new List<MapSummaryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadMapSummary(ReadEntity(reader)));
        }

        return (items, total);
    }

    public async Task<ContentEntityDto?> GetByLocalIndexAsync(
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
        return await GetSingleAsync(sql, importRunId, entityType, localIndex, null, cancellationToken);
    }

    public async Task<ContentEntityDto?> GetByContentKeyAsync(
        long importRunId,
        string entityType,
        string contentKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, import_run_id, content_key, local_index, schema_version, data::text
                           FROM content_entity
                           WHERE import_run_id = @runId AND entity_type = @entityType AND content_key = @contentKey;
                           """;
        return await GetSingleAsync(sql, importRunId, entityType, null, contentKey, cancellationToken);
    }

    private async Task<ContentEntityDto?> GetSingleAsync(
        string sql,
        long importRunId,
        string entityType,
        long? localIndex,
        string? contentKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        if (localIndex is not null) command.Parameters.AddWithValue("localIndex", localIndex.Value);
        if (contentKey is not null) command.Parameters.AddWithValue("contentKey", contentKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
    }

    private static async Task<long> CountAsync(
        NpgsqlConnection connection,
        string where,
        long importRunId,
        string entityType,
        string? query,
        CancellationToken cancellationToken,
        bool addExactQuery = false)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT count(*)::bigint FROM content_entity WHERE {where};",
            connection);
        command.Parameters.AddWithValue("runId", importRunId);
        command.Parameters.AddWithValue("entityType", entityType);
        AddQueryParameter(command, query, addExactQuery);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static void AddQueryParameter(
        NpgsqlCommand command,
        string? query,
        bool addExactQuery = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        var escaped = query
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        command.Parameters.AddWithValue("pattern", $"%{escaped}%");
        if (addExactQuery)
        {
            command.Parameters.AddWithValue("exactQuery", query);
        }
    }

    private static MapSummaryDto ReadMapSummary(ContentEntityDto entity)
    {
        var data = entity.Data;
        return new MapSummaryDto(
            entity.LocalIndex,
            entity.ContentKey,
            ReadInt(data, "Index") ?? checked((int)entity.LocalIndex),
            ReadString(data, "FileName"),
            ReadString(data, "Title"));
    }

    private static int? ReadInt(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static string? ReadString(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static ImportRunDto ReadImportRun(NpgsqlDataReader reader)
    {
        return new ImportRunDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetDateTime(8));
    }

    private static ContentEntityDto ReadEntity(NpgsqlDataReader reader)
    {
        var json = reader.GetString(5);
        using var document = JsonDocument.Parse(json);
        return new ContentEntityDto(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt32(4),
            document.RootElement.Clone());
    }
}
