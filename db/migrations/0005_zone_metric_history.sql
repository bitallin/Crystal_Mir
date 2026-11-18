-- Persistent zone heartbeat samples and the indexes used by health/trend queries.

CREATE TABLE IF NOT EXISTS zone_metric_sample (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id text NOT NULL REFERENCES zone (zone_id) ON DELETE CASCADE,
    observed_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT now(),
    status text NOT NULL,
    active_release_id bigint REFERENCES content_release (id) ON DELETE SET NULL,
    active_content_hash text,
    player_count integer NOT NULL,
    tick_p95_ms double precision NOT NULL,
    error_rate double precision NOT NULL,
    metrics_source text,
    restart_failures integer,
    zone_pid integer,
    metadata jsonb,
    CONSTRAINT ck_zone_metric_sample_status CHECK (status IN ('online', 'degraded', 'offline')),
    CONSTRAINT ck_zone_metric_sample_player_count CHECK (player_count >= 0),
    CONSTRAINT ck_zone_metric_sample_tick_p95 CHECK (tick_p95_ms >= 0),
    CONSTRAINT ck_zone_metric_sample_error_rate CHECK (error_rate >= 0 AND error_rate <= 1),
    CONSTRAINT ck_zone_metric_sample_restart_failures CHECK (restart_failures IS NULL OR restart_failures >= 0),
    CONSTRAINT ck_zone_metric_sample_zone_pid CHECK (zone_pid IS NULL OR zone_pid > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_zone_metric_sample_observed
    ON zone_metric_sample (zone_id, observed_at);

CREATE INDEX IF NOT EXISTS ix_zone_metric_sample_zone_observed
    ON zone_metric_sample (zone_id, observed_at DESC);

CREATE INDEX IF NOT EXISTS ix_zone_metric_sample_recorded
    ON zone_metric_sample (recorded_at);
