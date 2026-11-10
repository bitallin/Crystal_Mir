using System.Net;
using Npgsql;
using NpgsqlTypes;

namespace Crystal.Control.Api.Auth;

public sealed class AuthStore
{
    private const int MaxFailedLogins = 5;
    private const long AccountMutationLockKey = 1129531731;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly NpgsqlDataSource _dataSource;

    public AuthStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<AccountRecord?> GetAccountByUsernameAsync(
        string usernameNormalized,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await GetAccountAsync(
            connection,
            transaction: null,
            "a.username_normalized = @value",
            "value",
            usernameNormalized,
            cancellationToken);
    }

    public async Task<AccountRecord?> GetAccountByIdAsync(long accountId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await GetAccountAsync(
            connection,
            transaction: null,
            "a.id = @value",
            "value",
            accountId,
            cancellationToken);
    }

    public async Task<bool> AnyAccountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM control_account);", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task RecordLoginFailureAsync(long accountId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE control_account
            SET failed_login_count = failed_login_count + 1,
                locked_until = CASE
                    WHEN failed_login_count + 1 >= @threshold THEN now() + @lockout
                    ELSE locked_until
                END,
                updated_at = now()
            WHERE id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("threshold", MaxFailedLogins);
        command.Parameters.Add(new NpgsqlParameter("lockout", NpgsqlDbType.Interval)
        {
            Value = LockoutDuration
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordLoginSuccessAsync(long accountId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE control_account
            SET failed_login_count = 0,
                locked_until = NULL,
                last_login_at = now(),
                updated_at = now()
            WHERE id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", accountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid> CreateSessionAsync(
        long accountId,
        string refreshTokenHash,
        DateTime refreshTokenExpiresAt,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO control_session
                (id, account_id, refresh_token_hash, refresh_token_expires_at, client_ip, user_agent)
            VALUES
                (@id, @accountId, @refreshHash, @refreshExpiresAt, @clientIp, @userAgent);
            """,
            connection);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("refreshHash", refreshTokenHash);
        command.Parameters.AddWithValue("refreshExpiresAt", refreshTokenExpiresAt);
        command.Parameters.Add(new NpgsqlParameter("clientIp", NpgsqlDbType.Inet)
        {
            Value = InetValue(clientIp)
        });
        command.Parameters.AddWithValue("userAgent", (object?)Truncate(userAgent, 512) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return sessionId;
    }

    public async Task<SessionAccountRecord?> RotateRefreshSessionAsync(
        string currentRefreshTokenHash,
        string replacementRefreshTokenHash,
        DateTime replacementRefreshTokenExpiresAt,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid sessionId;
        long accountId;
        string accountStatus;
        bool expired;
        bool revoked;
        var found = false;

        await using (var command = new NpgsqlCommand(
                         """
                         SELECT s.id, s.account_id, a.status, (s.refresh_token_expires_at <= now()) AS expired,
                                s.revoked_at IS NOT NULL AS revoked
                         FROM control_session s
                         JOIN control_account a ON a.id = s.account_id
                         WHERE s.refresh_token_hash = @refreshHash
                         FOR UPDATE OF s;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("refreshHash", currentRefreshTokenHash);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    found = true;
                    sessionId = reader.GetGuid(0);
                    accountId = reader.GetInt64(1);
                    accountStatus = reader.GetString(2);
                    expired = reader.GetBoolean(3);
                    revoked = reader.GetBoolean(4);
                }
                else
                {
                    sessionId = Guid.Empty;
                    accountId = 0;
                    accountStatus = string.Empty;
                    expired = false;
                    revoked = false;
                }
            }
        }

        if (!found)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (expired || revoked || !string.Equals(accountStatus, "active", StringComparison.Ordinal))
        {
            await RevokeSessionAsync(
                connection,
                transaction,
                sessionId,
                accountId,
                expired ? "refresh_expired" : revoked ? "already_revoked" : "account_disabled",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var account = await GetAccountAsync(
            connection,
            transaction,
            "a.id = @value",
            "value",
            accountId,
            cancellationToken);
        if (account is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE control_session
                         SET refresh_token_hash = @refreshHash,
                             refresh_token_expires_at = @refreshExpiresAt,
                             client_ip = @clientIp,
                             user_agent = @userAgent,
                             last_seen_at = now()
                         WHERE id = @id;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("id", sessionId);
            command.Parameters.AddWithValue("refreshHash", replacementRefreshTokenHash);
            command.Parameters.AddWithValue("refreshExpiresAt", replacementRefreshTokenExpiresAt);
            command.Parameters.Add(new NpgsqlParameter("clientIp", NpgsqlDbType.Inet)
            {
                Value = InetValue(clientIp)
            });
            command.Parameters.AddWithValue("userAgent", (object?)Truncate(userAgent, 512) ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SessionAccountRecord(account, sessionId);
    }

    public async Task RevokeSessionAsync(
        Guid sessionId,
        long accountId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await RevokeSessionAsync(connection, transaction: null, sessionId, accountId, reason, cancellationToken);
    }

    public async Task<bool> IsSessionActiveAsync(
        Guid sessionId,
        long accountId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM control_session s
                JOIN control_account a ON a.id = s.account_id
                WHERE s.id = @id
                  AND s.account_id = @accountId
                  AND s.revoked_at IS NULL
                  AND s.refresh_token_expires_at > now()
                  AND a.status = 'active'
            );
            """,
            connection);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("accountId", accountId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<AccountRecord> CreateAccountAsync(
        string username,
        string usernameNormalized,
        string? email,
        string displayName,
        string passwordHash,
        IReadOnlyList<string> roleCodes,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long accountId;
        try
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO control_account
                    (username, username_normalized, email, display_name, password_hash)
                VALUES
                    (@username, @usernameNormalized, @email, @displayName, @passwordHash)
                RETURNING id;
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("username", username);
            command.Parameters.AddWithValue("usernameNormalized", usernameNormalized);
            command.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
            command.Parameters.AddWithValue("displayName", displayName);
            command.Parameters.AddWithValue("passwordHash", passwordHash);
            accountId = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DuplicateAccountException(username, exception);
        }

        var roleIds = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
                         "SELECT code, id FROM control_role WHERE code = ANY(@codes);",
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("codes", roleCodes.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                roleIds[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        var unknownRoles = roleCodes.Where(code => !roleIds.ContainsKey(code)).ToArray();
        if (unknownRoles.Length > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new UnknownRoleException(unknownRoles);
        }

        foreach (var roleCode in roleCodes.Distinct(StringComparer.Ordinal))
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO control_account_role (account_id, role_id) VALUES (@accountId, @roleId);",
                connection,
                transaction);
            command.Parameters.AddWithValue("accountId", accountId);
            command.Parameters.AddWithValue("roleId", roleIds[roleCode]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAccountByIdAsync(accountId, cancellationToken)
               ?? throw new InvalidOperationException($"Created account {accountId} could not be reloaded.");
    }

    public async Task<AccountStatusChangeResult> SetAccountStatusAsync(
        long accountId,
        string status,
        long? actorAccountId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await AcquireAccountMutationLockAsync(connection, transaction, cancellationToken);
        var account = await GetAccountForUpdateAsync(connection, transaction, accountId, cancellationToken);
        if (account is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        var previousStatus = account.Status;
        if (string.Equals(previousStatus, status, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new AccountStatusChangeResult(account, previousStatus, 0);
        }

        if (string.Equals(status, "disabled", StringComparison.Ordinal) && actorAccountId == accountId)
        {
            throw new AccountLifecycleConflictException("You cannot disable the account used for this request.");
        }

        if (string.Equals(status, "disabled", StringComparison.Ordinal) &&
            string.Equals(previousStatus, "active", StringComparison.Ordinal) &&
            account.Roles.Contains("platform_admin", StringComparer.Ordinal) &&
            await CountActivePlatformAdminsAsync(connection, transaction, cancellationToken) <= 1)
        {
            throw new AccountLifecycleConflictException("The last active platform administrator cannot be disabled.");
        }

        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE control_account
                         SET status = @status,
                             failed_login_count = CASE WHEN @status = 'active' THEN 0 ELSE failed_login_count END,
                             locked_until = CASE WHEN @status = 'active' THEN NULL ELSE locked_until END,
                             updated_at = now()
                         WHERE id = @id;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("id", accountId);
            command.Parameters.AddWithValue("status", status);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var revokedSessionCount = string.Equals(status, "disabled", StringComparison.Ordinal)
            ? await RevokeAccountSessionsAsync(
                connection,
                transaction,
                accountId,
                "account_disabled",
                exceptSessionId: null,
                cancellationToken)
            : 0;

        await transaction.CommitAsync(cancellationToken);
        var updated = await GetAccountAsync(
                          connection,
                          transaction: null,
                          "a.id = @value",
                          "value",
                          accountId,
                          cancellationToken)
                      ?? throw new InvalidOperationException($"Account {accountId} could not be reloaded after status update.");
        return new AccountStatusChangeResult(updated, previousStatus, revokedSessionCount);
    }

    public async Task<AccountRoleChangeResult> ReplaceAccountRolesAsync(
        long accountId,
        IReadOnlyList<string> roleCodes,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await AcquireAccountMutationLockAsync(connection, transaction, cancellationToken);
        var account = await GetAccountForUpdateAsync(connection, transaction, accountId, cancellationToken);
        if (account is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        var normalizedRoles = roleCodes.Distinct(StringComparer.Ordinal).ToArray();
        var roleIds = await LoadRoleIdsAsync(connection, transaction, normalizedRoles, cancellationToken);
        var unknownRoles = normalizedRoles.Where(code => !roleIds.ContainsKey(code)).ToArray();
        if (unknownRoles.Length > 0)
        {
            throw new UnknownRoleException(unknownRoles);
        }

        var previousRoles = account.Roles;
        var changed = !previousRoles
            .OrderBy(role => role, StringComparer.Ordinal)
            .SequenceEqual(normalizedRoles.OrderBy(role => role, StringComparer.Ordinal), StringComparer.Ordinal);
        if (!changed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AccountRoleChangeResult(account, previousRoles, 0);
        }

        if (string.Equals(account.Status, "active", StringComparison.Ordinal) &&
            previousRoles.Contains("platform_admin", StringComparer.Ordinal) &&
            !normalizedRoles.Contains("platform_admin", StringComparer.Ordinal) &&
            await CountActivePlatformAdminsAsync(connection, transaction, cancellationToken) <= 1)
        {
            throw new AccountLifecycleConflictException("The last active platform administrator cannot lose the platform_admin role.");
        }

        await using (var command = new NpgsqlCommand(
                         "DELETE FROM control_account_role WHERE account_id = @accountId;",
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("accountId", accountId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var roleCode in normalizedRoles)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO control_account_role (account_id, role_id) VALUES (@accountId, @roleId);",
                connection,
                transaction);
            command.Parameters.AddWithValue("accountId", accountId);
            command.Parameters.AddWithValue("roleId", roleIds[roleCode]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var revokedSessionCount = await RevokeAccountSessionsAsync(
            connection,
            transaction,
            accountId,
            "roles_changed",
            exceptSessionId: null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var updated = await GetAccountAsync(
                          connection,
                          transaction: null,
                          "a.id = @value",
                          "value",
                          accountId,
                          cancellationToken)
                      ?? throw new InvalidOperationException($"Account {accountId} could not be reloaded after role update.");
        return new AccountRoleChangeResult(updated, previousRoles, revokedSessionCount);
    }

    public async Task<AccountPasswordChangeResult> ResetPasswordAsync(
        long accountId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var account = await GetAccountForUpdateAsync(connection, transaction, accountId, cancellationToken);
        if (account is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        await UpdatePasswordAsync(
            connection,
            transaction,
            accountId,
            passwordHash,
            cancellationToken);
        var revokedSessionCount = await RevokeAccountSessionsAsync(
            connection,
            transaction,
            accountId,
            "password_reset",
            exceptSessionId: null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var updated = await GetAccountAsync(
                          connection,
                          transaction: null,
                          "a.id = @value",
                          "value",
                          accountId,
                          cancellationToken)
                      ?? throw new InvalidOperationException($"Account {accountId} could not be reloaded after password reset.");
        return new AccountPasswordChangeResult(updated, revokedSessionCount);
    }

    public async Task<AccountPasswordChangeResult> ChangePasswordAsync(
        long accountId,
        string passwordHash,
        Guid? keepSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var account = await GetAccountForUpdateAsync(connection, transaction, accountId, cancellationToken);
        if (account is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        await UpdatePasswordAsync(
            connection,
            transaction,
            accountId,
            passwordHash,
            cancellationToken);
        var revokedSessionCount = await RevokeAccountSessionsAsync(
            connection,
            transaction,
            accountId,
            "password_changed",
            exceptSessionId: keepSessionId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var updated = await GetAccountAsync(
                          connection,
                          transaction: null,
                          "a.id = @value",
                          "value",
                          accountId,
                          cancellationToken)
                      ?? throw new InvalidOperationException($"Account {accountId} could not be reloaded after password change.");
        return new AccountPasswordChangeResult(updated, revokedSessionCount);
    }

    public async Task<IReadOnlyList<AccountListItem>> ListAccountsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT a.id, a.username, a.email, a.display_name, a.status, a.mfa_enabled,
                   a.last_login_at, a.created_at,
                   COALESCE(array_agg(DISTINCT r.code ORDER BY r.code) FILTER (WHERE r.code IS NOT NULL), '{}'::text[])
            FROM control_account a
            LEFT JOIN control_account_role ar ON ar.account_id = a.id
            LEFT JOIN control_role r ON r.id = ar.role_id
            GROUP BY a.id
            ORDER BY a.id
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("limit", limit);

        var accounts = new List<AccountListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new AccountListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.GetDateTime(7),
                reader.GetFieldValue<string[]>(8)));
        }

        return accounts;
    }

    public async Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT r.code, r.name, r.description,
                   COALESCE(array_agg(DISTINCT p.code ORDER BY p.code) FILTER (WHERE p.code IS NOT NULL), '{}'::text[])
            FROM control_role r
            LEFT JOIN control_role_permission rp ON rp.role_id = r.id
            LEFT JOIN control_permission p ON p.id = rp.permission_id
            GROUP BY r.id
            ORDER BY r.code;
            """,
            connection);

        var roles = new List<RoleDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleDefinition(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetFieldValue<string[]>(3)));
        }

        return roles;
    }

    private static async Task AcquireAccountMutationLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key::bigint);",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", AccountMutationLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AccountRecord?> GetAccountForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long accountId,
        CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
                         "SELECT 1 FROM control_account WHERE id = @id FOR UPDATE;",
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("id", accountId);
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        return await GetAccountAsync(
            connection,
            transaction,
            "a.id = @value",
            "value",
            accountId,
            cancellationToken);
    }

    private static async Task<Dictionary<string, long>> LoadRoleIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<string> roleCodes,
        CancellationToken cancellationToken)
    {
        var roleIds = new Dictionary<string, long>(StringComparer.Ordinal);
        if (roleCodes.Count == 0)
        {
            return roleIds;
        }

        await using var command = new NpgsqlCommand(
            "SELECT code, id FROM control_role WHERE code = ANY(@codes);",
            connection,
            transaction);
        command.Parameters.AddWithValue("codes", roleCodes.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roleIds[reader.GetString(0)] = reader.GetInt64(1);
        }

        return roleIds;
    }

    private static async Task<int> CountActivePlatformAdminsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)::int
            FROM control_account a
            JOIN control_account_role ar ON ar.account_id = a.id
            JOIN control_role r ON r.id = ar.role_id
            WHERE a.status = 'active' AND r.code = 'platform_admin';
            """,
            connection,
            transaction);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> RevokeAccountSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long accountId,
        string reason,
        Guid? exceptSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE control_session
            SET revoked_at = COALESCE(revoked_at, now()),
                revoked_reason = COALESCE(revoked_reason, @reason)
            WHERE account_id = @accountId
              AND revoked_at IS NULL
              AND (@exceptSessionId IS NULL OR id <> @exceptSessionId);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("reason", Truncate(reason, 128) ?? "revoked");
        command.Parameters.Add(new NpgsqlParameter("exceptSessionId", NpgsqlDbType.Uuid)
        {
            Value = exceptSessionId is null ? DBNull.Value : exceptSessionId.Value
        });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdatePasswordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long accountId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE control_account
            SET password_hash = @passwordHash,
                password_changed_at = now(),
                failed_login_count = 0,
                locked_until = NULL,
                updated_at = now()
            WHERE id = @id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("passwordHash", passwordHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AccountRecord?> GetAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string predicate,
        string parameterName,
        object value,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $$"""
             SELECT a.id, a.username, a.email, a.display_name, a.password_hash, a.status,
                    a.mfa_enabled, a.failed_login_count, a.locked_until, a.last_login_at, a.created_at,
                    COALESCE(array_agg(DISTINCT r.code ORDER BY r.code) FILTER (WHERE r.code IS NOT NULL), '{}'::text[]),
                    COALESCE(array_agg(DISTINCT p.code ORDER BY p.code) FILTER (WHERE p.code IS NOT NULL), '{}'::text[])
             FROM control_account a
             LEFT JOIN control_account_role ar ON ar.account_id = a.id
             LEFT JOIN control_role r ON r.id = ar.role_id
             LEFT JOIN control_role_permission rp ON rp.role_id = r.id
             LEFT JOIN control_permission p ON p.id = rp.permission_id
             WHERE {{predicate}}
             GROUP BY a.id;
             """,
            connection,
            transaction);
        command.Parameters.AddWithValue(parameterName, value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new AccountRecord(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            reader.GetDateTime(10),
            reader.GetFieldValue<string[]>(11),
            reader.GetFieldValue<string[]>(12));
    }

    private static async Task RevokeSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid sessionId,
        long accountId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE control_session
            SET revoked_at = COALESCE(revoked_at, now()),
                revoked_reason = COALESCE(revoked_reason, @reason)
            WHERE id = @id AND account_id = @accountId;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("reason", Truncate(reason, 128) ?? "revoked");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int length)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= length ? value : value[..length];
    }

    private static object InetValue(string? value)
    {
        return IPAddress.TryParse(value, out var address) ? address : DBNull.Value;
    }
}

public sealed record RoleDefinition(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions);

public sealed class DuplicateAccountException : Exception
{
    public DuplicateAccountException(string username, Exception innerException)
        : base($"An account named '{username}' already exists.", innerException)
    {
    }
}

public sealed class UnknownRoleException : Exception
{
    public UnknownRoleException(IReadOnlyList<string> roleCodes)
        : base($"Unknown role codes: {string.Join(", ", roleCodes)}.")
    {
        RoleCodes = roleCodes;
    }

    public IReadOnlyList<string> RoleCodes { get; }
}

public sealed class AccountNotFoundException : Exception
{
    public AccountNotFoundException(long accountId)
        : base($"Account {accountId} does not exist.")
    {
        AccountId = accountId;
    }

    public long AccountId { get; }
}

public sealed class AccountLifecycleConflictException : Exception
{
    public AccountLifecycleConflictException(string message)
        : base(message)
    {
    }
}
