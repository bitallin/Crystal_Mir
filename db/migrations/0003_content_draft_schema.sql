-- Crystal editable content drafts, revision history and release rollback metadata.
-- Idempotent migration. Applied after the control-plane identity schema.

CREATE TABLE IF NOT EXISTS content_draft (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    base_import_run_id bigint NOT NULL REFERENCES content_import_run (id) ON DELETE RESTRICT,
    status text NOT NULL DEFAULT 'editing',
    created_by bigint REFERENCES control_account (id) ON DELETE SET NULL,
    updated_by bigint REFERENCES control_account (id) ON DELETE SET NULL,
    entity_count integer NOT NULL DEFAULT 0,
    last_validation jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT content_draft_name_check CHECK (char_length(name) BETWEEN 1 AND 128),
    CONSTRAINT content_draft_status_check
        CHECK (status IN ('editing', 'validated', 'failed', 'archived')),
    CONSTRAINT content_draft_entity_count_check CHECK (entity_count >= 0)
);

CREATE INDEX IF NOT EXISTS ix_content_draft_status_updated
    ON content_draft (status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_content_draft_base_import
    ON content_draft (base_import_run_id, created_at DESC);

CREATE TABLE IF NOT EXISTS content_draft_entity (
    draft_id bigint NOT NULL REFERENCES content_draft (id) ON DELETE CASCADE,
    entity_type text NOT NULL,
    local_index bigint NOT NULL,
    content_key text NOT NULL,
    schema_version integer NOT NULL,
    data jsonb NOT NULL,
    revision bigint NOT NULL DEFAULT 1,
    updated_by bigint REFERENCES control_account (id) ON DELETE SET NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (draft_id, entity_type, content_key),
    CONSTRAINT content_draft_entity_index_unique UNIQUE (draft_id, entity_type, local_index),
    CONSTRAINT content_draft_entity_local_index_check CHECK (local_index >= 0),
    CONSTRAINT content_draft_entity_type_check CHECK (entity_type ~ '^[a-z][a-z0-9_-]{0,63}$'),
    CONSTRAINT content_draft_entity_revision_check CHECK (revision > 0),
    CONSTRAINT content_draft_entity_schema_version_check CHECK (schema_version > 0)
);

CREATE INDEX IF NOT EXISTS ix_content_draft_entity_type_index
    ON content_draft_entity (draft_id, entity_type, local_index);
CREATE INDEX IF NOT EXISTS ix_content_draft_entity_data_gin
    ON content_draft_entity USING gin (data jsonb_path_ops);

CREATE TABLE IF NOT EXISTS content_draft_revision (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    draft_id bigint NOT NULL REFERENCES content_draft (id) ON DELETE CASCADE,
    revision_number bigint NOT NULL,
    action text NOT NULL,
    entity_type text NOT NULL,
    local_index bigint NOT NULL,
    content_key text NOT NULL,
    before_data jsonb,
    after_data jsonb,
    actor_account_id bigint REFERENCES control_account (id) ON DELETE SET NULL,
    reason text,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT content_draft_revision_number_unique UNIQUE (draft_id, revision_number),
    CONSTRAINT content_draft_revision_action_check
        CHECK (action IN ('create', 'update', 'delete')),
    CONSTRAINT content_draft_revision_local_index_check CHECK (local_index >= 0)
);

CREATE INDEX IF NOT EXISTS ix_content_draft_revision_entity
    ON content_draft_revision (draft_id, entity_type, content_key, revision_number DESC);

ALTER TABLE content_release
    ADD COLUMN IF NOT EXISTS draft_id bigint REFERENCES content_draft (id) ON DELETE SET NULL;
ALTER TABLE content_release
    ADD COLUMN IF NOT EXISTS rolled_back_at timestamptz;
ALTER TABLE content_release
    ADD COLUMN IF NOT EXISTS rolled_back_by bigint REFERENCES control_account (id) ON DELETE SET NULL;
ALTER TABLE content_release
    ADD COLUMN IF NOT EXISTS rollback_reason text;
ALTER TABLE content_release
    ADD COLUMN IF NOT EXISTS rollback_target_id bigint REFERENCES content_release (id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_content_release_draft
    ON content_release (draft_id, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_content_release_single_active
    ON content_release ((true))
    WHERE status = 'active';
