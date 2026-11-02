BEGIN;

CREATE SCHEMA IF NOT EXISTS platform;

CREATE TABLE platform.customer_scope (
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, customer_tenant_id)
);

CREATE TABLE platform.metadata_records (
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    record_id uuid NOT NULL,
    record_kind text NOT NULL CHECK (record_kind IN (
        'profile', 'assessment', 'finding', 'exception', 'emergency_registry', 'plan', 'approval', 'operation_state')),
    version integer NOT NULL CHECK (version > 0),
    previous_version integer,
    payload jsonb NOT NULL,
    parent_kind text,
    parent_record_id uuid,
    parent_version integer,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, customer_tenant_id, record_id, version),
    UNIQUE (organization_id, customer_tenant_id, record_id, version, record_kind),
    FOREIGN KEY (organization_id, customer_tenant_id)
        REFERENCES platform.customer_scope (organization_id, customer_tenant_id),
    FOREIGN KEY (organization_id, customer_tenant_id, record_id, previous_version, record_kind)
        REFERENCES platform.metadata_records (organization_id, customer_tenant_id, record_id, version, record_kind),
    FOREIGN KEY (organization_id, customer_tenant_id, parent_record_id, parent_version, parent_kind)
        REFERENCES platform.metadata_records (organization_id, customer_tenant_id, record_id, version, record_kind),
    CHECK ((version = 1 AND previous_version IS NULL) OR
           (version > 1 AND previous_version = version - 1)),
    CHECK (record_kind <> 'approval' OR version = 1),
    CHECK ((parent_kind IS NULL AND parent_record_id IS NULL AND parent_version IS NULL) OR
           (parent_kind IS NOT NULL AND parent_record_id IS NOT NULL AND parent_version > 0)),
    CHECK (parent_kind IS NULL OR parent_kind IN (
        'profile', 'assessment', 'finding', 'exception', 'emergency_registry', 'plan', 'approval', 'operation_state'))
);

CREATE TABLE platform.encrypted_sessions (
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    user_object_id uuid NOT NULL,
    session_id uuid NOT NULL,
    ciphertext bytea NOT NULL CHECK (octet_length(ciphertext) > 0),
    nonce bytea NOT NULL CHECK (octet_length(nonce) > 0),
    authentication_tag bytea NOT NULL CHECK (octet_length(authentication_tag) > 0),
    wrapped_data_key bytea NOT NULL CHECK (octet_length(wrapped_data_key) > 0),
    key_id text NOT NULL CHECK (length(key_id) > 0),
    key_version integer NOT NULL CHECK (key_version > 0),
    expires_at_utc timestamptz NOT NULL,
    revoked_at_utc timestamptz,
    updated_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, customer_tenant_id, user_object_id, session_id),
    FOREIGN KEY (organization_id, customer_tenant_id)
        REFERENCES platform.customer_scope (organization_id, customer_tenant_id)
);

CREATE TABLE platform.evidence_objects (
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    evidence_id uuid NOT NULL,
    sha256 text NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    byte_length bigint NOT NULL CHECK (byte_length >= 0),
    content_type text NOT NULL,
    storage_key text NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, customer_tenant_id, evidence_id),
    FOREIGN KEY (organization_id, customer_tenant_id)
        REFERENCES platform.customer_scope (organization_id, customer_tenant_id)
);

CREATE TABLE platform.audit_events (
    ledger_sequence bigint GENERATED ALWAYS AS IDENTITY,
    organization_id uuid NOT NULL,
    customer_tenant_id uuid NOT NULL,
    event_id uuid NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    previous_hash text NOT NULL,
    canonical_payload text NOT NULL,
    event_hash text NOT NULL CHECK (event_hash ~ '^[0-9a-f]{64}$'),
    PRIMARY KEY (organization_id, customer_tenant_id, event_id),
    FOREIGN KEY (organization_id, customer_tenant_id)
        REFERENCES platform.customer_scope (organization_id, customer_tenant_id)
);

CREATE INDEX metadata_records_scope_kind_created_idx
    ON platform.metadata_records (organization_id, customer_tenant_id, record_kind, created_at_utc DESC);
CREATE INDEX audit_events_scope_time_idx
    ON platform.audit_events (organization_id, customer_tenant_id, occurred_at_utc DESC);
CREATE INDEX audit_events_scope_sequence_idx
    ON platform.audit_events (organization_id, customer_tenant_id, ledger_sequence);
CREATE INDEX encrypted_sessions_expiry_idx ON platform.encrypted_sessions (expires_at_utc);

CREATE FUNCTION platform.reject_immutable_row_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'immutable platform history cannot be updated or deleted';
END;
$$;

CREATE TRIGGER metadata_records_immutable
    BEFORE UPDATE OR DELETE ON platform.metadata_records
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_row_mutation();
CREATE TRIGGER evidence_objects_immutable
    BEFORE UPDATE OR DELETE ON platform.evidence_objects
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_row_mutation();
CREATE TRIGGER audit_events_immutable
    BEFORE UPDATE OR DELETE ON platform.audit_events
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_row_mutation();

CREATE FUNCTION platform.protect_session_revocation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.revoked_at_utc IS NOT NULL AND NEW.revoked_at_utc IS DISTINCT FROM OLD.revoked_at_utc THEN
        RAISE EXCEPTION 'a revoked session cannot be reactivated or have its revocation timestamp changed';
    END IF;
    IF OLD.revoked_at_utc IS NOT NULL AND
       (NEW.ciphertext IS DISTINCT FROM OLD.ciphertext OR NEW.nonce IS DISTINCT FROM OLD.nonce OR
        NEW.authentication_tag IS DISTINCT FROM OLD.authentication_tag OR
        NEW.wrapped_data_key IS DISTINCT FROM OLD.wrapped_data_key OR NEW.key_id IS DISTINCT FROM OLD.key_id OR
        NEW.key_version IS DISTINCT FROM OLD.key_version OR NEW.expires_at_utc IS DISTINCT FROM OLD.expires_at_utc) THEN
        RAISE EXCEPTION 'revoked session material is immutable';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER encrypted_sessions_revoke_once
    BEFORE UPDATE ON platform.encrypted_sessions
    FOR EACH ROW EXECUTE FUNCTION platform.protect_session_revocation();

ALTER TABLE platform.customer_scope ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.metadata_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.encrypted_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.evidence_objects ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.customer_scope FORCE ROW LEVEL SECURITY;
ALTER TABLE platform.metadata_records FORCE ROW LEVEL SECURITY;
ALTER TABLE platform.encrypted_sessions FORCE ROW LEVEL SECURITY;
ALTER TABLE platform.evidence_objects FORCE ROW LEVEL SECURITY;
ALTER TABLE platform.audit_events FORCE ROW LEVEL SECURITY;

CREATE POLICY customer_scope_isolation ON platform.customer_scope
    USING (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
       AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid)
    WITH CHECK (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
            AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid);
CREATE POLICY metadata_records_isolation ON platform.metadata_records
    USING (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
       AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid)
    WITH CHECK (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
            AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid);
CREATE POLICY encrypted_sessions_isolation ON platform.encrypted_sessions
    USING (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
       AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid
       AND user_object_id = NULLIF(current_setting('platform.user_object_id', true), '')::uuid)
    WITH CHECK (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
            AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid
            AND user_object_id = NULLIF(current_setting('platform.user_object_id', true), '')::uuid);
CREATE POLICY evidence_objects_isolation ON platform.evidence_objects
    USING (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
       AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid)
    WITH CHECK (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
            AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid);
CREATE POLICY audit_events_isolation ON platform.audit_events
    USING (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
       AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid)
    WITH CHECK (organization_id = NULLIF(current_setting('platform.organization_id', true), '')::uuid
            AND customer_tenant_id = NULLIF(current_setting('platform.customer_tenant_id', true), '')::uuid);

GRANT USAGE ON SCHEMA platform TO platform_app;
GRANT SELECT ON platform.customer_scope TO platform_app;
GRANT SELECT, INSERT ON platform.metadata_records TO platform_app;
GRANT SELECT, INSERT, UPDATE ON platform.encrypted_sessions TO platform_app;
GRANT SELECT, INSERT ON platform.evidence_objects TO platform_app;
GRANT SELECT, INSERT ON platform.audit_events TO platform_app;
GRANT USAGE, SELECT ON SEQUENCE platform.audit_events_ledger_sequence_seq TO platform_app;
REVOKE UPDATE, DELETE, TRUNCATE ON platform.metadata_records, platform.evidence_objects, platform.audit_events FROM platform_app;

COMMIT;