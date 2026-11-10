-- Crystal control-plane identity, RBAC, session and audit schema.
-- Idempotent migration. PostgreSQL 12+.

CREATE TABLE IF NOT EXISTS control_account (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username text NOT NULL,
    username_normalized text NOT NULL,
    email text,
    display_name text NOT NULL,
    password_hash text NOT NULL,
    status text NOT NULL DEFAULT 'active',
    mfa_enabled boolean NOT NULL DEFAULT false,
    failed_login_count integer NOT NULL DEFAULT 0,
    locked_until timestamptz,
    last_login_at timestamptz,
    password_changed_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT control_account_username_length_check CHECK (char_length(username) BETWEEN 3 AND 64),
    CONSTRAINT control_account_username_normalized_check CHECK (username_normalized = lower(username_normalized)),
    CONSTRAINT control_account_display_name_check CHECK (char_length(display_name) BETWEEN 1 AND 128),
    CONSTRAINT control_account_status_check CHECK (status IN ('active', 'disabled')),
    CONSTRAINT control_account_failed_login_count_check CHECK (failed_login_count >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_control_account_username_normalized
    ON control_account (username_normalized);
CREATE INDEX IF NOT EXISTS ix_control_account_status
    ON control_account (status, created_at DESC);

CREATE TABLE IF NOT EXISTS control_role (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code text NOT NULL,
    name text NOT NULL,
    description text,
    is_system boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT control_role_code_check CHECK (code ~ '^[a-z][a-z0-9_-]{1,63}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_control_role_code ON control_role (code);

CREATE TABLE IF NOT EXISTS control_permission (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code text NOT NULL,
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT control_permission_code_check CHECK (code ~ '^[a-z][a-z0-9_-]*:[a-z][a-z0-9_-]*$')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_control_permission_code ON control_permission (code);

CREATE TABLE IF NOT EXISTS control_role_permission (
    role_id bigint NOT NULL REFERENCES control_role (id) ON DELETE CASCADE,
    permission_id bigint NOT NULL REFERENCES control_permission (id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE IF NOT EXISTS control_account_role (
    account_id bigint NOT NULL REFERENCES control_account (id) ON DELETE CASCADE,
    role_id bigint NOT NULL REFERENCES control_role (id) ON DELETE RESTRICT,
    assigned_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (account_id, role_id)
);

CREATE INDEX IF NOT EXISTS ix_control_account_role_role
    ON control_account_role (role_id, account_id);

CREATE TABLE IF NOT EXISTS control_session (
    id uuid PRIMARY KEY,
    account_id bigint NOT NULL REFERENCES control_account (id) ON DELETE CASCADE,
    refresh_token_hash text NOT NULL,
    refresh_token_expires_at timestamptz NOT NULL,
    client_ip inet,
    user_agent text,
    created_at timestamptz NOT NULL DEFAULT now(),
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz,
    revoked_reason text,
    CONSTRAINT control_session_refresh_hash_check CHECK (refresh_token_hash ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_control_session_refresh_token_hash
    ON control_session (refresh_token_hash);
CREATE INDEX IF NOT EXISTS ix_control_session_account_active
    ON control_session (account_id, revoked_at, refresh_token_expires_at DESC);

CREATE TABLE IF NOT EXISTS audit_event (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    actor_account_id bigint REFERENCES control_account (id) ON DELETE SET NULL,
    actor_label text,
    actor_ip inet,
    action text NOT NULL,
    resource_type text,
    resource_id text,
    before_hash text,
    after_hash text,
    request_id text,
    result text NOT NULL,
    reason text,
    user_agent text,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT audit_event_result_check CHECK (result IN ('success', 'failure', 'denied'))
);

CREATE INDEX IF NOT EXISTS ix_audit_event_occurred_at
    ON audit_event (occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_event_actor
    ON audit_event (actor_account_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_event_action
    ON audit_event (action, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_event_resource
    ON audit_event (resource_type, resource_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION reject_audit_event_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'audit_event is append-only';
END;
$$;

DROP TRIGGER IF EXISTS audit_event_no_update_delete ON audit_event;
CREATE TRIGGER audit_event_no_update_delete
    BEFORE UPDATE OR DELETE ON audit_event
    FOR EACH ROW
    EXECUTE FUNCTION reject_audit_event_mutation();

DROP TRIGGER IF EXISTS audit_event_no_truncate ON audit_event;
CREATE TRIGGER audit_event_no_truncate
    BEFORE TRUNCATE ON audit_event
    FOR EACH STATEMENT
    EXECUTE FUNCTION reject_audit_event_mutation();

CREATE OR REPLACE VIEW control_account_permission AS
SELECT DISTINCT
    a.id AS account_id,
    p.code AS permission_code
FROM control_account a
JOIN control_account_role ar ON ar.account_id = a.id
JOIN control_role_permission rp ON rp.role_id = ar.role_id
JOIN control_permission p ON p.id = rp.permission_id;

INSERT INTO control_permission (code, description)
VALUES
    ('account:read', 'Read control-plane accounts and role assignments'),
    ('account:write', 'Create and manage control-plane accounts'),
    ('role:read', 'Read roles and permissions'),
    ('role:write', 'Manage roles and permission assignments'),
    ('audit:read', 'Read control-plane audit events'),
    ('content:read', 'Read imported content'),
    ('content:write', 'Edit content drafts'),
    ('release:read', 'Read content releases'),
    ('release:write', 'Create and validate content releases'),
    ('release:publish', 'Publish content releases'),
    ('release:rollback', 'Rollback content releases'),
    ('asset:read', 'Read content assets'),
    ('asset:write', 'Upload and manage content assets'),
    ('zone:read', 'Read zones and zone operations'),
    ('zone:write', 'Create and manage zones'),
    ('zone:merge', 'Run zone merge operations'),
    ('payment:read', 'Read payment orders and ledgers'),
    ('payment:write', 'Manage payment operations'),
    ('payment:refund', 'Approve and execute payment refunds'),
    ('gm:operate', 'Run GM operations'),
    ('system:configure', 'Change platform configuration')
ON CONFLICT (code) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO control_role (code, name, description, is_system)
VALUES
    ('platform_admin', 'Platform administrator', 'Full control-plane administration', true),
    ('content_author', 'Content author', 'Edit drafts, assets and release candidates', true),
    ('release_approver', 'Release approver', 'Approve, publish and roll back releases', true),
    ('gm', 'Game master', 'Operate zones and run GM actions', true),
    ('finance', 'Finance', 'Manage orders, ledgers and refunds', true),
    ('auditor', 'Auditor', 'Read-only access to audit and platform records', true)
ON CONFLICT (code) DO UPDATE
SET name = EXCLUDED.name,
    description = EXCLUDED.description,
    is_system = EXCLUDED.is_system;

INSERT INTO control_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM control_role r
CROSS JOIN control_permission p
WHERE r.code = 'platform_admin'
ON CONFLICT DO NOTHING;

INSERT INTO control_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM control_role r
JOIN control_permission p ON p.code IN (
    'content:read', 'content:write', 'release:read', 'release:write',
    'asset:read', 'asset:write'
)
WHERE r.code = 'content_author'
ON CONFLICT DO NOTHING;

INSERT INTO control_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM control_role r
JOIN control_permission p ON p.code IN (
    'content:read', 'release:read', 'release:publish', 'release:rollback', 'asset:read'
)
WHERE r.code = 'release_approver'
ON CONFLICT DO NOTHING;

INSERT INTO control_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM control_role r
JOIN control_permission p ON p.code IN ('content:read', 'zone:read', 'gm:operate')
WHERE r.code = 'gm'
ON CONFLICT DO NOTHING;

INSERT INTO control_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM control_role r
JOIN control_permission p ON p.code IN ('payment:read', 'payment:write', 'payment:refund')
WHERE r.code = 'finance'
ON CONFLICT DO NOTHING;

INSERT INTO control_role_permission (role_id, permission_id)
SELECT r.id, p.id
FROM control_role r
JOIN control_permission p ON p.code IN (
    'account:read', 'role:read', 'audit:read', 'content:read',
    'release:read', 'zone:read', 'payment:read'
)
WHERE r.code = 'auditor'
ON CONFLICT DO NOTHING;
