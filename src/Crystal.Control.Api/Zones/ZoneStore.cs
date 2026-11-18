using System.Text.Json;
using Crystal.Agent.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Zones;

public sealed class ZoneNotFoundException : Exception
{
    public ZoneNotFoundException(string message) : base(message)
    {
    }
}

public sealed class ZoneCommandConflictException : Exception
{
    public ZoneCommandConflictException(string message) : base(message)
    {
    }
}

public sealed class ZoneStore
{
    private const string ZoneColumns = """
        z.zone_id, z.zone_name, z.agent_id, z.agent_version, z.protocol_version,
        z.content_schema_version,
        CASE
            WHEN z.last_heartbeat_at IS NULL OR z.last_heartbeat_at < now() - interval '30 seconds'
                THEN 'offline'
            ELSE z.status
        END,
        z.started_at, z.last_heartbeat_at,
        z.active_release_id, ar.version, z.active_content_hash,
        z.staged_release_id, sr.version, z.staged_content_hash,
        z.player_count, z.tick_p95_ms, z.error_rate, z.capabilities,
        z.metadata::text, z.created_at, z.updated_at
        """;

    private const string ZoneJoins = """
        LEFT JOIN content_release ar ON ar.id = z.active_release_id
        LEFT JOIN content_release sr ON sr.id = z.staged_release_id
        """;

    private const string CommandColumns = """
        command_id, zone_id, command_type, status, release_id, payload::text,
        issued_at, expires_at, attempts, claimed_at, completed_at, error, result_payload::text
        """;

    private readonly NpgsqlDataSource _dataSource;

    public ZoneStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<ZoneDto> RegisterAsync(
        AgentRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO zone (
                zone_id, zone_name, agent_id, agent_version, protocol_version,
                content_schema_version, status, started_at, last_heartbeat_at,
                capabilities, created_at, updated_at)
            VALUES (
                @zoneId, @zoneName, @agentId, @agentVersion, @protocolVersion,
                @contentSchemaVersion, 'online', @startedAt, now(),
                @capabilities, now(), now())
            ON CONFLICT (zone_id) DO UPDATE SET
                zone_name = EXCLUDED.zone_name,
                agent_id = EXCLUDED.agent_id,
                agent_version = EXCLUDED.agent_version,
                protocol_version = EXCLUDED.protocol_version,
                content_schema_version = EXCLUDED.content_schema_version,
                status = 'online',
                started_at = EXCLUDED.started_at,
                last_heartbeat_at = now(),
                capabilities = EXCLUDED.capabilities,
                updated_at = now();
            """,
            connection);
        command.Parameters.AddWithValue("zoneId", request.ZoneId);
        command.Parameters.AddWithValue("zoneName", request.ZoneName);
        command.Parameters.AddWithValue("agentId", request.AgentId);
        command.Parameters.AddWithValue("agentVersion", request.AgentVersion);
        command.Parameters.AddWithValue("protocolVersion", request.ProtocolVersion);
        command.Parameters.AddWithValue("contentSchemaVersion", request.ContentSchemaVersion);
        command.Parameters.AddWithValue("startedAt", request.StartedAt.ToUniversalTime());
        command.Parameters.Add(new NpgsqlParameter("capabilities", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = request.Capabilities?.ToArray() ?? Array.Empty<string>()
        });
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(connection, request.ZoneId, cancellationToken)
               ?? throw new ZoneNotFoundException($"Zone {request.ZoneId} disappeared after registration.");
    }

    public async Task<ZoneDto?> HeartbeatAsync(
        string zoneId,
        AgentHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var metadataJson = request.Metadata is null
            ? null
            : JsonSerializer.Serialize(request.Metadata, AgentContractJson.Options);
        await using var command = new NpgsqlCommand(
            """
            UPDATE zone
            SET status = @status,
                last_heartbeat_at = now(),
                active_release_id = @activeReleaseId,
                active_content_hash = @activeContentHash,
                player_count = @playerCount,
                tick_p95_ms = @tickP95Ms,
                error_rate = @errorRate,
                metadata = @metadata,
                updated_at = now()
            WHERE zone_id = @zoneId AND agent_id = @agentId;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("zoneId", zoneId);
        command.Parameters.AddWithValue("agentId", request.AgentId);
        command.Parameters.AddWithValue("status", request.Status);
        command.Parameters.Add(new NpgsqlParameter("activeReleaseId", NpgsqlDbType.Bigint)
        {
            Value = request.ActiveReleaseId ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("activeContentHash", NpgsqlDbType.Text)
        {
            Value = (object?)request.ActiveContentHash ?? DBNull.Value
        });
        command.Parameters.AddWithValue("playerCount", request.PlayerCount);
        command.Parameters.AddWithValue("tickP95Ms", request.TickP95Ms);
        command.Parameters.AddWithValue("errorRate", request.ErrorRate);
        command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb)
        {
            Value = metadataJson is null ? DBNull.Value : metadataJson
        });

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await InsertMetricSampleAsync(connection, transaction, zoneId, request, metadataJson, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(connection, zoneId, cancellationToken);
    }

    public async Task<IReadOnlyList<ZoneMetricSampleDto>> ListMetricSamplesAsync(
        string zoneId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        if (!await ZoneExistsAsync(connection, zoneId, cancellationToken))
        {
            throw new ZoneNotFoundException($"Zone {zoneId} does not exist.");
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT zone_id, observed_at, recorded_at, status, active_release_id,
                   active_content_hash, player_count, tick_p95_ms, error_rate,
                   metrics_source, restart_failures, zone_pid
            FROM (
                SELECT zone_id, observed_at, recorded_at, status, active_release_id,
                       active_content_hash, player_count, tick_p95_ms, error_rate,
                       metrics_source, restart_failures, zone_pid
                FROM zone_metric_sample
                WHERE zone_id = @zoneId
                  AND observed_at >= @from
                  AND observed_at <= @to
                ORDER BY observed_at DESC
                LIMIT @limit
            ) samples
            ORDER BY observed_at;
            """,
            connection);
        command.Parameters.AddWithValue("zoneId", zoneId);
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        command.Parameters.AddWithValue("limit", limit);

        var samples = new List<ZoneMetricSampleDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            samples.Add(ReadMetricSample(reader));
        }

        return samples;
    }

    public async Task<IReadOnlyList<ZoneDto>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {ZoneColumns}
            FROM zone z
            {ZoneJoins}
            ORDER BY z.zone_id
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("limit", limit);

        var zones = new List<ZoneDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            zones.Add(ReadZone(reader));
        }

        return zones;
    }

    public async Task<ZoneDto?> GetAsync(string zoneId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, zoneId, cancellationToken);
    }

    public async Task<IReadOnlyList<ZoneCommandDto>> ListCommandsAsync(
        string zoneId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        if (!await ZoneExistsAsync(connection, zoneId, cancellationToken))
        {
            throw new ZoneNotFoundException($"Zone {zoneId} does not exist.");
        }

        await ExpireCommandsAsync(connection, zoneId, cancellationToken);

        await using var command = new NpgsqlCommand(
            $"""
            SELECT {CommandColumns}
            FROM zone_command
            WHERE zone_id = @zoneId
            ORDER BY issued_at DESC
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("zoneId", zoneId);
        command.Parameters.AddWithValue("limit", limit);

        var commands = new List<ZoneCommandDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            commands.Add(ReadCommand(reader));
        }

        return commands;
    }

    public async Task<ZoneCommandDto> CreateCommandAsync(
        string zoneId,
        string type,
        long? releaseId,
        JsonElement? payload,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        if (!await ZoneExistsAsync(connection, zoneId, cancellationToken))
        {
            throw new ZoneNotFoundException($"Zone {zoneId} does not exist.");
        }

        var commandId = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO zone_command (
                command_id, zone_id, command_type, release_id, payload, status,
                issued_at, available_at, expires_at)
            VALUES (
                @commandId, @zoneId, @commandType, @releaseId, @payload, 'queued',
                now(), now(), @expiresAt)
            RETURNING command_id;
            """,
            connection);
        command.Parameters.AddWithValue("commandId", commandId);
        command.Parameters.AddWithValue("zoneId", zoneId);
        command.Parameters.AddWithValue("commandType", type);
        command.Parameters.Add(new NpgsqlParameter("releaseId", NpgsqlDbType.Bigint)
        {
            Value = releaseId ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = payload is null ? DBNull.Value : payload.Value.GetRawText()
        });
        command.Parameters.AddWithValue("expiresAt", expiresAt.ToUniversalTime());

        try
        {
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
                  string.Equals(exception.ConstraintName, "ux_zone_command_one_lifecycle", StringComparison.Ordinal))
        {
            throw new ZoneCommandConflictException(
                $"Zone {zoneId} already has an unfinished lifecycle command.");
        }

        return await GetCommandAsync(connection, commandId, cancellationToken)
               ?? throw new ZoneCommandConflictException($"Command {commandId} disappeared after creation.");
    }

    public async Task<IReadOnlyList<AgentCommand>> ClaimCommandsAsync(
        string zoneId,
        int limit,
        int leaseSeconds,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ZoneExistsAsync(connection, zoneId, cancellationToken, transaction))
        {
            throw new ZoneNotFoundException($"Zone {zoneId} does not exist.");
        }

        await ExpireCommandsAsync(connection, zoneId, cancellationToken, transaction);

        var commands = new List<AgentCommand>();
        await using (var claim = new NpgsqlCommand(
                         """
                         WITH candidate AS (
                             SELECT command_id
                             FROM zone_command
                             WHERE zone_id = @zoneId
                               AND expires_at > now()
                               AND (
                                   status = 'queued' OR
                                   (status = 'delivered' AND lease_expires_at <= now())
                               )
                             ORDER BY issued_at
                             LIMIT @limit
                             FOR UPDATE SKIP LOCKED
                         )
                         UPDATE zone_command c
                         SET status = 'delivered',
                             attempts = attempts + 1,
                             claimed_at = now(),
                             lease_expires_at = now() + make_interval(secs => @leaseSeconds)
                         FROM candidate
                         WHERE c.command_id = candidate.command_id
                         RETURNING c.command_id, c.zone_id, c.command_type, c.issued_at,
                                   c.expires_at, c.payload::text;
                         """,
                         connection,
                         transaction))
        {
            claim.Parameters.AddWithValue("zoneId", zoneId);
            claim.Parameters.AddWithValue("limit", limit);
            claim.Parameters.AddWithValue("leaseSeconds", leaseSeconds);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                commands.Add(ReadAgentCommand(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return commands;
    }

    public async Task<ZoneCommandDto?> CompleteCommandAsync(
        string zoneId,
        Guid commandId,
        AgentCommandResult result,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string commandType;
        long? releaseId;
        string currentStatus;
        bool found;
        await using (var select = new NpgsqlCommand(
                         """
                         SELECT command_type, release_id, status
                         FROM zone_command
                         WHERE command_id = @commandId AND zone_id = @zoneId
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            select.Parameters.AddWithValue("commandId", commandId);
            select.Parameters.AddWithValue("zoneId", zoneId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            found = await reader.ReadAsync(cancellationToken);
            if (!found)
            {
                commandType = string.Empty;
                releaseId = null;
                currentStatus = string.Empty;
            }
            else
            {
                commandType = reader.GetString(0);
                releaseId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                currentStatus = reader.GetString(2);
            }
        }

        if (!found)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (AgentCommandStatuses.Final.Contains(currentStatus, StringComparer.Ordinal))
        {
            if (!string.Equals(currentStatus, result.Status, StringComparison.Ordinal))
            {
                throw new ZoneCommandConflictException(
                    $"Command {commandId} is already '{currentStatus}', cannot change it to '{result.Status}'.");
            }

            await transaction.CommitAsync(cancellationToken);
            return await GetCommandAsync(connection, commandId, cancellationToken);
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE zone_command
                         SET status = @status,
                             completed_at = @completedAt,
                             error = @error,
                             result_payload = @resultPayload
                         WHERE command_id = @commandId AND zone_id = @zoneId;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("commandId", commandId);
            update.Parameters.AddWithValue("zoneId", zoneId);
            update.Parameters.AddWithValue("status", result.Status);
            update.Parameters.AddWithValue(
                "completedAt",
                (result.CompletedAt ?? DateTimeOffset.UtcNow).ToUniversalTime());
            update.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text)
            {
                Value = (object?)result.Error ?? DBNull.Value
            });
            update.Parameters.Add(new NpgsqlParameter("resultPayload", NpgsqlDbType.Jsonb)
            {
                Value = result.Payload is null ? DBNull.Value : result.Payload.Value.GetRawText()
            });
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (string.Equals(result.Status, AgentCommandStatuses.Succeeded, StringComparison.Ordinal))
        {
            await ApplySuccessfulCommandAsync(
                connection,
                transaction,
                zoneId,
                commandType,
                releaseId,
                result.Payload,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetCommandAsync(connection, commandId, cancellationToken);
    }

    private static async Task ApplySuccessfulCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string zoneId,
        string commandType,
        long? releaseId,
        JsonElement? resultPayload,
        CancellationToken cancellationToken)
    {
        var contentHash = ReadString(resultPayload, "contentHash");
        string sql;
        if (commandType is AgentCommandTypes.StageContent)
        {
            sql = """
                  UPDATE zone
                  SET staged_release_id = @releaseId,
                      staged_content_hash = COALESCE(@contentHash,
                          (SELECT content_hash FROM content_release WHERE id = @releaseId)),
                      updated_at = now()
                  WHERE zone_id = @zoneId AND @releaseId IS NOT NULL;
                  """;
        }
        else if (commandType is AgentCommandTypes.ActivateContent or AgentCommandTypes.RollbackContent)
        {
            sql = """
                  UPDATE zone
                  SET active_release_id = @releaseId,
                      active_content_hash = COALESCE(@contentHash,
                          (SELECT content_hash FROM content_release WHERE id = @releaseId)),
                      status = 'online',
                      updated_at = now()
                  WHERE zone_id = @zoneId AND @releaseId IS NOT NULL;
                  """;
        }
        else if (commandType is AgentCommandTypes.ReloadContent)
        {
            sql = """
                  UPDATE zone
                  SET active_content_hash = COALESCE(@contentHash, active_content_hash),
                      status = 'online',
                      updated_at = now()
                  WHERE zone_id = @zoneId;
                  """;
        }
        else if (commandType is AgentCommandTypes.StopZone)
        {
            sql = """
                  UPDATE zone
                  SET status = 'degraded',
                      active_release_id = NULL,
                      active_content_hash = NULL,
                      updated_at = now()
                  WHERE zone_id = @zoneId;
                  """;
        }
        else
        {
            return;
        }

        await using var update = new NpgsqlCommand(sql, connection, transaction);
        update.Parameters.AddWithValue("zoneId", zoneId);
        update.Parameters.Add(new NpgsqlParameter("releaseId", NpgsqlDbType.Bigint)
        {
            Value = releaseId ?? (object)DBNull.Value
        });
        update.Parameters.Add(new NpgsqlParameter("contentHash", NpgsqlDbType.Text)
        {
            Value = (object?)contentHash ?? DBNull.Value
        });
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExpireCommandsAsync(
        NpgsqlConnection connection,
        string zoneId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var expire = new NpgsqlCommand(
            """
            UPDATE zone_command
            SET status = 'expired',
                completed_at = now(),
                error = COALESCE(error, 'Command expired before completion.')
            WHERE zone_id = @zoneId
              AND status IN ('queued', 'delivered')
              AND expires_at <= now();
            """,
            connection,
            transaction);
        expire.Parameters.AddWithValue("zoneId", zoneId);
        await expire.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ZoneExistsAsync(
        NpgsqlConnection connection,
        string zoneId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM zone WHERE zone_id = @zoneId);",
            connection,
            transaction);
        command.Parameters.AddWithValue("zoneId", zoneId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task InsertMetricSampleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string zoneId,
        AgentHeartbeatRequest request,
        string? metadataJson,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO zone_metric_sample (
                zone_id, observed_at, status, active_release_id, active_content_hash,
                player_count, tick_p95_ms, error_rate, metrics_source,
                restart_failures, zone_pid, metadata)
            VALUES (
                @zoneId, @observedAt, @status, @activeReleaseId, @activeContentHash,
                @playerCount, @tickP95Ms, @errorRate, @metricsSource,
                @restartFailures, @zonePid, @metadata)
            ON CONFLICT (zone_id, observed_at) DO NOTHING;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("zoneId", zoneId);
        command.Parameters.AddWithValue("observedAt", request.ObservedAt.ToUniversalTime());
        command.Parameters.AddWithValue("status", request.Status);
        command.Parameters.Add(new NpgsqlParameter("activeReleaseId", NpgsqlDbType.Bigint)
        {
            Value = request.ActiveReleaseId ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("activeContentHash", NpgsqlDbType.Text)
        {
            Value = (object?)request.ActiveContentHash ?? DBNull.Value
        });
        command.Parameters.AddWithValue("playerCount", request.PlayerCount);
        command.Parameters.AddWithValue("tickP95Ms", request.TickP95Ms);
        command.Parameters.AddWithValue("errorRate", request.ErrorRate);
        command.Parameters.Add(new NpgsqlParameter("metricsSource", NpgsqlDbType.Text)
        {
            Value = (object?)ReadMetadataString(request.Metadata, "metricsSource") ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("restartFailures", NpgsqlDbType.Integer)
        {
            Value = ReadMetadataInt(request.Metadata, "restartFailures") ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("zonePid", NpgsqlDbType.Integer)
        {
            Value = ReadMetadataInt(request.Metadata, "zonePid") ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb)
        {
            Value = metadataJson is null ? DBNull.Value : metadataJson
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ZoneDto?> GetAsync(
        NpgsqlConnection connection,
        string zoneId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {ZoneColumns}
            FROM zone z
            {ZoneJoins}
            WHERE z.zone_id = @zoneId;
            """,
            connection);
        command.Parameters.AddWithValue("zoneId", zoneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadZone(reader) : null;
    }

    private static async Task<ZoneCommandDto?> GetCommandAsync(
        NpgsqlConnection connection,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {CommandColumns} FROM zone_command WHERE command_id = @commandId;",
            connection);
        command.Parameters.AddWithValue("commandId", commandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCommand(reader) : null;
    }

    private static ZoneDto ReadZone(NpgsqlDataReader reader)
    {
        return new ZoneDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : ReadTimestamp(reader, 7),
            reader.IsDBNull(8) ? null : ReadTimestamp(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetInt64(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetInt32(15),
            reader.GetDouble(16),
            reader.GetDouble(17),
            reader.GetFieldValue<string[]>(18),
            ReadJson(reader, 19),
            ReadTimestamp(reader, 20),
            ReadTimestamp(reader, 21));
    }

    private static AgentCommand ReadAgentCommand(NpgsqlDataReader reader)
    {
        return new AgentCommand(
            reader.GetGuid(0).ToString(),
            reader.GetString(1),
            reader.GetString(2),
            ReadTimestamp(reader, 3),
            ReadTimestamp(reader, 4),
            ReadJson(reader, 5));
    }

    private static ZoneCommandDto ReadCommand(NpgsqlDataReader reader)
    {
        return new ZoneCommandDto(
            reader.GetGuid(0).ToString(),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            ReadJson(reader, 5),
            ReadTimestamp(reader, 6),
            ReadTimestamp(reader, 7),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : ReadTimestamp(reader, 9),
            reader.IsDBNull(10) ? null : ReadTimestamp(reader, 10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            ReadJson(reader, 12));
    }

    private static ZoneMetricSampleDto ReadMetricSample(NpgsqlDataReader reader)
    {
        return new ZoneMetricSampleDto(
            reader.GetString(0),
            ReadTimestamp(reader, 1),
            ReadTimestamp(reader, 2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11));
    }

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }

    private static JsonElement? ReadJson(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        using var document = JsonDocument.Parse(reader.GetString(ordinal));
        return document.RootElement.Clone();
    }

    private static string? ReadString(JsonElement? payload, string propertyName)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value) return null;
        return value.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? ReadMetadataString(IReadOnlyDictionary<string, string>? metadata, string name)
    {
        return metadata is not null && metadata.TryGetValue(name, out var value) ? value : null;
    }

    private static int? ReadMetadataInt(IReadOnlyDictionary<string, string>? metadata, string name)
    {
        return metadata is not null &&
               metadata.TryGetValue(name, out var value) &&
               int.TryParse(value, out var parsed)
            ? parsed
            : null;
    }
}
