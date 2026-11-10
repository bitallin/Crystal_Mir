using System.Text.Json;
using Crystal.Content;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.LegacyImporter;

public static class PostgresContentStore
{
    public static async Task<long> ImportAsync(
        string connectionString,
        IReadOnlyList<string> migrationPaths,
        ContentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeConnectionString(connectionString);

        await using var connection = new NpgsqlConnection(normalized);
        await connection.OpenAsync(cancellationToken);

        foreach (var migrationPath in migrationPaths)
        {
            var schemaSql = File.ReadAllText(migrationPath);
            var schemaVersion = Path.GetFileNameWithoutExtension(migrationPath);
            var schemaChecksum = ContentJson.Sha256Hex(File.ReadAllBytes(migrationPath));
            await ApplySchemaAsync(connection, schemaVersion, schemaSql, schemaChecksum, cancellationToken);
        }

        var legacyVersion = TryGetLegacyVersion(snapshot.Manifest.Legacy);
        var runId = await CreateImportRunAsync(
            connection,
            transaction: null,
            snapshot,
            legacyVersion,
            cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await CopyEntitiesAsync(connection, transaction, runId, snapshot.Entities, cancellationToken);
            await MarkImportedAsync(connection, transaction, runId, snapshot.Entities.Count, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return runId;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            await MarkFailedAsync(normalized, runId, exception, cancellationToken);
            throw;
        }
    }

    public static string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Database connection string is empty.", nameof(connectionString));
        }

        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var credentials = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : string.Empty,
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = Uri.UnescapeDataString(parts[0]);
            var value = Uri.UnescapeDataString(parts[1]);
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = Enum.Parse<SslMode>(value, ignoreCase: true);
            }
        }

        return builder.ConnectionString;
    }

    private static async Task ApplySchemaAsync(
        NpgsqlConnection connection,
        string version,
        string schemaSql,
        string checksum,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(schemaSql, connection, transaction))
        {
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
                         """
                         INSERT INTO schema_migrations (version, checksum)
                         VALUES (@version, @checksum)
                         ON CONFLICT (version) DO NOTHING;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("checksum", checksum);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
                         "SELECT checksum FROM schema_migrations WHERE version = @version;",
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("version", version);
            var existing = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(existing, checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Migration '{version}' was already applied with checksum '{existing}', current file is '{checksum}'.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> CreateImportRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ContentSnapshot snapshot,
        int? legacyVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO content_import_run
                (source, source_hash, legacy_version, schema_version, status, summary)
            VALUES
                (@source, @sourceHash, @legacyVersion, @schemaVersion, 'running', @summary)
            RETURNING id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("source", snapshot.Manifest.Source);
        command.Parameters.AddWithValue("sourceHash", (object?)snapshot.Manifest.SourceHash ?? DBNull.Value);
        command.Parameters.AddWithValue("legacyVersion", (object?)legacyVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("schemaVersion", snapshot.Manifest.SchemaVersion);
        command.Parameters.AddWithValue(
            "summary",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(snapshot.Manifest, ContentJson.CompactOptions));

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task CopyEntitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long runId,
        IReadOnlyList<ContentEntity> entities,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            """
            COPY content_entity
                (import_run_id, entity_type, local_index, content_key, schema_version, data)
            FROM STDIN (FORMAT BINARY)
            """,
            cancellationToken);

        foreach (var entity in entities)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(runId, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(entity.EntityType, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(entity.LocalIndex, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(entity.ContentKey, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(entity.SchemaVersion, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(entity.Data.GetRawText(), NpgsqlDbType.Jsonb, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task MarkImportedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long runId,
        int entityCount,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE content_import_run
            SET status = 'imported',
                entity_count = @entityCount,
                finished_at = now()
            WHERE id = @id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("entityCount", entityCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkFailedAsync(
        string connectionString,
        long runId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                UPDATE content_import_run
                SET status = 'failed',
                    error = @error,
                    finished_at = now()
                WHERE id = @id;
                """,
                connection);
            command.Parameters.AddWithValue("id", runId);
            command.Parameters.AddWithValue("error", exception.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original import failure.
        }
    }

    private static int? TryGetLegacyVersion(JsonElement? legacy)
    {
        if (legacy is not { ValueKind: JsonValueKind.Object } element) return null;
        return element.TryGetProperty("loadVersion", out var version) &&
               version.TryGetInt32(out var value)
            ? value
            : null;
    }
}
