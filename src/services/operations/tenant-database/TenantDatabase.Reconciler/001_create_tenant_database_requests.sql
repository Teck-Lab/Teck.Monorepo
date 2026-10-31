CREATE TABLE IF NOT EXISTS tenant_database_requests (
    subscription_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    desired_state jsonb NOT NULL,
    phase text NOT NULL,
    detail text NULL,
    received_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    lease_until timestamptz NULL,
    source_order_id uuid NOT NULL,
    proof text NOT NULL,
    previous_source_order_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    evidence_id uuid NULL,
    readiness_published_at timestamptz NULL,
    last_cluster_uid uuid NULL,
    purge_approval_id uuid NULL,
    purge_proof text NULL,
    purge_message jsonb NULL,
    purge_backup_uid uuid NULL,
    purge_restore_uid uuid NULL,
    purge_backup_name text NULL,
    purge_verified_at timestamptz NULL,
    purged_at timestamptz NULL
);

ALTER TABLE tenant_database_requests
    ADD COLUMN IF NOT EXISTS previous_source_order_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS evidence_id uuid NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS readiness_published_at timestamptz NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS last_cluster_uid uuid NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_approval_id uuid NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_proof text NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_message jsonb NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_backup_uid uuid NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_restore_uid uuid NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_backup_name text NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purge_verified_at timestamptz NULL;
ALTER TABLE tenant_database_requests ADD COLUMN IF NOT EXISTS purged_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_tenant_database_requests_pending
    ON tenant_database_requests (updated_at, received_at)
    WHERE phase NOT IN ('Ready', 'NotManaged', 'Retained', 'Purging', 'Purged');

CREATE INDEX IF NOT EXISTS ix_tenant_database_requests_unpublished
    ON tenant_database_requests (updated_at, received_at)
    WHERE phase = 'Ready' AND readiness_published_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_tenant_database_requests_pending_purge
    ON tenant_database_requests (updated_at, received_at)
    WHERE phase IN ('Retained', 'Purging') AND purge_message IS NOT NULL;
