-- Zone registration, heartbeats and the durable Agent command queue.

CREATE TABLE IF NOT EXISTS zone (
    zone_id text PRIMARY KEY,
    zone_name text NOT NULL,
    agent_id text NOT NULL,
    agent_version text NOT NULL,
    protocol_version integer NOT NULL,
    content_schema_version integer NOT NULL,
    status text NOT NULL DEFAULT 'degraded',
    started_at timestamptz,
    last_heartbeat_at timestamptz,
    active_release_id bigint REFERENCES content_release (id) ON DELETE SET NULL,
    active_content_hash text,
    staged_release_id bigint REFERENCES content_release (id) ON DELETE SET NULL,
    staged_content_hash text,
    player_count integer NOT NULL DEFAULT 0,
    tick_p95_ms double precision NOT NULL DEFAULT 0,
    error_rate double precision NOT NULL DEFAULT 0,
    capabilities text[] NOT NULL DEFAULT '{}',
    metadata jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_zone_status CHECK (status IN ('online', 'degraded', 'offline')),
    CONSTRAINT ck_zone_zone_id CHECK (zone_id ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')
);

CREATE INDEX IF NOT EXISTS ix_zone_status_heartbeat
    ON zone (status, last_heartbeat_at DESC);

CREATE TABLE IF NOT EXISTS zone_command (
    command_id uuid PRIMARY KEY,
    zone_id text NOT NULL REFERENCES zone (zone_id) ON DELETE CASCADE,
    command_type text NOT NULL,
    release_id bigint REFERENCES content_release (id) ON DELETE SET NULL,
    payload jsonb,
    status text NOT NULL DEFAULT 'queued',
    attempts integer NOT NULL DEFAULT 0,
    issued_at timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    claimed_at timestamptz,
    lease_expires_at timestamptz,
    completed_at timestamptz,
    error text,
    result_payload jsonb,
    CONSTRAINT ck_zone_command_status CHECK (
        status IN ('queued', 'delivered', 'succeeded', 'failed', 'rejected', 'expired')
    ),
    CONSTRAINT ck_zone_command_type CHECK (
        command_type IN ('content.stage', 'content.activate', 'content.rollback',
                         'content.reload', 'zone.stop')
    )
);

CREATE INDEX IF NOT EXISTS ix_zone_command_poll
    ON zone_command (zone_id, status, available_at, issued_at);

CREATE INDEX IF NOT EXISTS ix_zone_command_history
    ON zone_command (zone_id, issued_at DESC);

CREATE INDEX IF NOT EXISTS ix_zone_command_lease
    ON zone_command (status, lease_expires_at)
    WHERE status = 'delivered';

CREATE UNIQUE INDEX IF NOT EXISTS ux_zone_command_one_lifecycle
    ON zone_command (zone_id)
    WHERE command_type IN ('content.activate', 'content.rollback', 'content.reload', 'zone.stop')
      AND status IN ('queued', 'delivered');
