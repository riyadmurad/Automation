BEGIN;

CREATE TABLE platform.operator_customer_assignments (
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    operator_tenant_id uuid NOT NULL,
    operator_object_id uuid NOT NULL,
    roles text[] NOT NULL CHECK (cardinality(roles) > 0),
    requires_independent_approval boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, customer_tenant_id, operator_tenant_id, operator_object_id),
    FOREIGN KEY (organization_id, customer_tenant_id)
        REFERENCES platform.customer_scope (organization_id, customer_tenant_id),
    CHECK (roles <@ ARRAY['Viewer', 'Assessor', 'Approver', 'Operator', 'Admin']::text[])
);

CREATE TABLE platform.customer_connections (
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    purpose text NOT NULL CHECK (purpose IN ('assessment', 'remediation')),
    credential_reference text NOT NULL CHECK (length(credential_reference) > 0),
    granted_scopes text[] NOT NULL DEFAULT '{}',
    health text NOT NULL CHECK (health IN ('unknown', 'healthy', 'degraded', 'revoked', 'failed')),
    is_active boolean NOT NULL DEFAULT false,
    last_checked_at_utc timestamptz,
    updated_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, customer_tenant_id, purpose),
    FOREIGN KEY (organization_id, customer_tenant_id)
        REFERENCES platform.customer_scope (organization_id, customer_tenant_id)
);

CREATE INDEX operator_assignment_lookup_idx
    ON platform.operator_customer_assignments (operator_tenant_id, operator_object_id, organization_id, customer_tenant_id)
    WHERE is_active;

ALTER TABLE platform.operator_customer_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.operator_customer_assignments FORCE ROW LEVEL SECURITY;
CREATE POLICY operator_assignment_isolation ON platform.operator_customer_assignments
    USING (operator_tenant_id = NULLIF(current_setting('platform.operator_tenant_id', true), '')::uuid
       AND operator_object_id = NULLIF(current_setting('platform.operator_object_id', true), '')::uuid)
    WITH CHECK (operator_tenant_id = NULLIF(current_setting('platform.operator_tenant_id', true), '')::uuid
            AND operator_object_id = NULLIF(current_setting('platform.operator_object_id', true), '')::uuid);

ALTER TABLE platform.customer_connections ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.customer_connections FORCE ROW LEVEL SECURITY;
CREATE POLICY customer_connection_isolation ON platform.customer_connections
    USING (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
       AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid
       AND EXISTS (
           SELECT 1 FROM platform.operator_customer_assignments assignment
           WHERE assignment.organization_id = customer_connections.organization_id
             AND assignment.customer_tenant_id = customer_connections.customer_tenant_id
             AND assignment.operator_tenant_id = NULLIF(current_setting('platform.operator_tenant_id', true), '')::uuid
             AND assignment.operator_object_id = NULLIF(current_setting('platform.operator_object_id', true), '')::uuid
             AND assignment.is_active))
    WITH CHECK (false);

GRANT SELECT ON platform.operator_customer_assignments, platform.customer_connections TO platform_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON platform.operator_customer_assignments, platform.customer_connections FROM platform_app;

COMMIT;