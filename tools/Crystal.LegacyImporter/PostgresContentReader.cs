using System.Text.Json;
using Crystal.Content;
using Npgsql;

namespace Crystal.LegacyImporter;

public sealed record PostgresContentSnapshot(long ImportRunId, ContentSnapshot Snapshot);

public static class PostgresContentReader
{
    public static async Task<PostgresContentSnapshot> ReadLatestImportedAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var normalized = PostgresContentStore.NormalizeConnectionString(connectionString);
        await using var connection = new NpgsqlConnection(normalized);
        await connection.OpenAsync(cancellationToken);

        long importRunId;
        ContentSnapshotManifest manifest;
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT id, summary::text
                         FROM content_import_run
                         WHERE status = 'imported'
                         ORDER BY finished_at DESC NULLS LAST, id DESC
                         LIMIT 1;
                         """,
                         connection))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException("No imported content snapshot exists in PostgreSQL.");
            }

            importRunId = reader.GetInt64(0);
            manifest = JsonSerializer.Deserialize<ContentSnapshotManifest>(
                reader.GetString(1),
                ContentJson.IndentedOptions)
                ?? throw new InvalidDataException($"Cannot read snapshot summary for import run {importRunId}.");
        }

        if (manifest.FormatVersion != ContentSnapshotFormat.FormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported PostgreSQL snapshot format {manifest.FormatVersion}; " +
                $"expected {ContentSnapshotFormat.FormatVersion}.");
        }

        var entities = new List<ContentEntity>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT entity_type, local_index, content_key, schema_version, data::text
                         FROM content_entity
                         WHERE import_run_id = @importRunId
                         ORDER BY entity_type, local_index, content_key;
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("importRunId", importRunId);
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
                    Data = ContentJson.Canonicalize(document.RootElement)
                });
            }
        }

        var snapshot = new ContentSnapshot
        {
            Manifest = manifest,
            Entities = entities
        };
        var issues = ContentSnapshotValidator.Validate(snapshot);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                "PostgreSQL snapshot validation failed: " +
                string.Join("; ", issues.Select(issue => issue.Message)));
        }

        return new PostgresContentSnapshot(importRunId, snapshot);
    }
}
