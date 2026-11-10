using System.Net;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Audit;

public sealed class AuditStore
{
    private readonly NpgsqlDataSource _dataSource;

    public AuditStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task WriteAsync(AuditEventWrite entry, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_event
                (actor_account_id, actor_label, actor_ip, action, resource_type, resource_id,
                 before_hash, after_hash, request_id, result, reason, user_agent, metadata)
            VALUES
                (@actorAccountId, @actorLabel, @actorIp, @action, @resourceType, @resourceId,
                 @beforeHash, @afterHash, @requestId, @result, @reason, @userAgent, @metadata);
            """,
            connection);
        command.Parameters.AddWithValue("actorAccountId", (object?)entry.ActorAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("actorLabel", (object?)Truncate(entry.ActorLabel, 128) ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("actorIp", NpgsqlDbType.Inet)
        {
            Value = IPAddress.TryParse(entry.ActorIp, out var address) ? address : DBNull.Value
        });
        command.Parameters.AddWithValue("action", Truncate(entry.Action, 128)!);
        command.Parameters.AddWithValue("resourceType", (object?)Truncate(entry.ResourceType, 128) ?? DBNull.Value);
        command.Parameters.AddWithValue("resourceId", (object?)Truncate(entry.ResourceId, 256) ?? DBNull.Value);
        command.Parameters.AddWithValue("beforeHash", (object?)Truncate(entry.BeforeHash, 128) ?? DBNull.Value);
        command.Parameters.AddWithValue("afterHash", (object?)Truncate(entry.AfterHash, 128) ?? DBNull.Value);
        command.Parameters.AddWithValue("requestId", (object?)Truncate(entry.RequestId, 128) ?? DBNull.Value);
        command.Parameters.AddWithValue("result", entry.Result);
        command.Parameters.AddWithValue("reason", (object?)Truncate(entry.Reason, 512) ?? DBNull.Value);
        command.Parameters.AddWithValue("userAgent", (object?)Truncate(entry.UserAgent, 512) ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(entry.Metadata ?? new { })
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEventDto>> QueryAsync(
        int limit,
        string? action,
        string? result,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, occurred_at, actor_account_id, actor_label, host(actor_ip), action,
                   resource_type, resource_id, request_id, result, reason, metadata::text
            FROM audit_event
            WHERE (@action IS NULL OR action = @action)
              AND (@result IS NULL OR result = @result)
              AND (@actorAccountId IS NULL OR actor_account_id = @actorAccountId)
            ORDER BY id DESC
            LIMIT @limit;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("action", NpgsqlDbType.Text)
        {
            Value = (object?)action ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("result", NpgsqlDbType.Text)
        {
            Value = (object?)result ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("actorAccountId", NpgsqlDbType.Bigint)
        {
            Value = actorAccountId is null ? DBNull.Value : actorAccountId.Value
        });
        command.Parameters.AddWithValue("limit", limit);

        var events = new List<AuditEventDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var metadata = JsonDocument.Parse(reader.GetString(11));
            events.Add(new AuditEventDto(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                metadata.RootElement.Clone()));
        }

        return events;
    }

    private static string? Truncate(string? value, int length)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= length ? value : value[..length];
    }
}

public sealed record AuditEventWrite(
    long? ActorAccountId,
    string? ActorLabel,
    string? ActorIp,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string? BeforeHash,
    string? AfterHash,
    string? RequestId,
    string Result,
    string? Reason,
    string? UserAgent,
    object? Metadata);

public sealed record AuditEventDto(
    long Id,
    DateTime OccurredAt,
    long? ActorAccountId,
    string? ActorLabel,
    string? ActorIp,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string? RequestId,
    string Result,
    string? Reason,
    JsonElement Metadata);
