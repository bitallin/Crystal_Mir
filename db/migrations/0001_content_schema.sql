-- Crystal control-plane content schema.
-- Idempotent migration. Applied by Crystal.LegacyImporter and any future migration runner.
-- PostgreSQL 16+.

CREATE TABLE IF NOT EXISTS schema_migrations (
    version text PRIMARY KEY,
    checksum text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS content_import_run (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source text NOT NULL,
    source_hash text,
    legacy_version integer,
    schema_version integer NOT NULL DEFAULT 1,
    status text NOT NULL DEFAULT 'running',
    entity_count integer NOT NULL DEFAULT 0,
    summary jsonb NOT NULL DEFAULT '{}'::jsonb,
    error text,
    started_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz,
    CONSTRAINT content_import_run_status_check
        CHECK (status IN ('running', 'imported', 'failed'))
);

CREATE INDEX IF NOT EXISTS ix_content_import_run_started_at
    ON content_import_run (started_at DESC);
CREATE INDEX IF NOT EXISTS ix_content_import_run_source_hash
    ON content_import_run (source_hash);

CREATE TABLE IF NOT EXISTS content_entity (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    import_run_id bigint NOT NULL REFERENCES content_import_run (id) ON DELETE CASCADE,
    entity_type text NOT NULL,
    local_index bigint NOT NULL,
    content_key text NOT NULL,
    schema_version integer NOT NULL,
    data jsonb NOT NULL,
    legacy jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT content_entity_local_index_check CHECK (local_index >= 0),
    CONSTRAINT content_entity_type_check CHECK (entity_type ~ '^[a-z][a-z0-9_-]{0,63}$'),
    CONSTRAINT content_entity_import_key_unique UNIQUE (import_run_id, entity_type, content_key),
    CONSTRAINT content_entity_import_index_unique UNIQUE (import_run_id, entity_type, local_index)
);

CREATE INDEX IF NOT EXISTS ix_content_entity_type_key
    ON content_entity (entity_type, content_key);
CREATE INDEX IF NOT EXISTS ix_content_entity_import_type
    ON content_entity (import_run_id, entity_type, local_index);
CREATE INDEX IF NOT EXISTS ix_content_entity_data_gin
    ON content_entity USING gin (data jsonb_path_ops);

CREATE TABLE IF NOT EXISTS content_release (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    import_run_id bigint REFERENCES content_import_run (id) ON DELETE SET NULL,
    version text NOT NULL UNIQUE,
    status text NOT NULL DEFAULT 'draft',
    schema_version integer NOT NULL DEFAULT 1,
    format_version integer NOT NULL DEFAULT 1,
    content_hash text NOT NULL,
    manifest_object_key text,
    created_by bigint,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    notes text,
    CONSTRAINT content_release_status_check
        CHECK (status IN ('draft', 'validating', 'compiled', 'signed', 'published', 'active', 'archived', 'failed', 'rolled_back'))
);

CREATE INDEX IF NOT EXISTS ix_content_release_status
    ON content_release (status, created_at DESC);

CREATE TABLE IF NOT EXISTS content_release_entity (
    release_id bigint NOT NULL REFERENCES content_release (id) ON DELETE CASCADE,
    entity_type text NOT NULL,
    local_index bigint NOT NULL,
    content_key text NOT NULL,
    payload jsonb NOT NULL,
    PRIMARY KEY (release_id, entity_type, content_key),
    CONSTRAINT content_release_entity_index_unique UNIQUE (release_id, entity_type, local_index)
);

CREATE INDEX IF NOT EXISTS ix_content_release_entity_key
    ON content_release_entity (entity_type, content_key);

CREATE TABLE IF NOT EXISTS asset (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sha256 text NOT NULL UNIQUE,
    object_key text NOT NULL,
    asset_type text NOT NULL,
    size_bytes bigint,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    license text,
    source text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT asset_sha256_check CHECK (sha256 ~ '^[0-9a-f]{64}$')
);

CREATE TABLE IF NOT EXISTS content_release_asset (
    release_id bigint NOT NULL REFERENCES content_release (id) ON DELETE CASCADE,
    asset_id bigint NOT NULL REFERENCES asset (id) ON DELETE RESTRICT,
    role text NOT NULL DEFAULT 'runtime',
    PRIMARY KEY (release_id, asset_id)
);

CREATE OR REPLACE VIEW content_item AS
SELECT import_run_id, content_key, local_index, data, created_at
FROM content_entity
WHERE entity_type = 'item';

CREATE OR REPLACE VIEW content_monster AS
SELECT import_run_id, content_key, local_index, data, created_at
FROM content_entity
WHERE entity_type = 'monster';

CREATE OR REPLACE VIEW content_map AS
SELECT import_run_id, content_key, local_index, data, created_at
FROM content_entity
WHERE entity_type = 'map';

CREATE OR REPLACE VIEW content_npc AS
SELECT import_run_id, content_key, local_index, data, created_at
FROM content_entity
WHERE entity_type = 'npc';

CREATE OR REPLACE VIEW content_magic AS
SELECT import_run_id, content_key, local_index, data, created_at
FROM content_entity
WHERE entity_type = 'magic';

CREATE OR REPLACE VIEW content_quest AS
SELECT import_run_id, content_key, local_index, data, created_at
FROM content_entity
WHERE entity_type = 'quest';
