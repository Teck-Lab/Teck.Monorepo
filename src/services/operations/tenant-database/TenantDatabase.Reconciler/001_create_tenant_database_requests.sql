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
    proof text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_tenant_database_requests_pending
    ON tenant_database_requests (updated_at, received_at)
    WHERE phase NOT IN ('Ready', 'NotManaged', 'Retained');
