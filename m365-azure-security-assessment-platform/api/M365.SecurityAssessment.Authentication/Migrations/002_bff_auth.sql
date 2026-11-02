BEGIN;

CREATE TABLE platform.oidc_transactions (
    transaction_id uuid PRIMARY KEY,
    state_sha256 text NOT NULL UNIQUE CHECK (state_sha256 ~ '^[0-9a-f]{64}$'),
    ciphertext bytea NOT NULL CHECK (octet_length(ciphertext) > 0),
    nonce bytea NOT NULL CHECK (octet_length(nonce) = 12),
    authentication_tag bytea NOT NULL CHECK (octet_length(authentication_tag) = 16),
    wrapped_data_key bytea NOT NULL CHECK (octet_length(wrapped_data_key) > 0),
    key_id text NOT NULL CHECK (length(key_id) > 0),
    key_version integer NOT NULL CHECK (key_version > 0),
    expires_at_utc timestamptz NOT NULL,
    consumed_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE platform.bff_auth_sessions (
    operator_tenant_id uuid NOT NULL,
    operator_object_id uuid NOT NULL,
    session_id uuid NOT NULL,
    ciphertext bytea NOT NULL CHECK (octet_length(ciphertext) > 0),
    nonce bytea NOT NULL CHECK (octet_length(nonce) = 12),
    authentication_tag bytea NOT NULL CHECK (octet_length(authentication_tag) = 16),
    wrapped_data_key bytea NOT NULL CHECK (octet_length(wrapped_data_key) > 0),
    key_id text NOT NULL CHECK (length(key_id) > 0),
    key_version integer NOT NULL CHECK (key_version > 0),
    expires_at_utc timestamptz NOT NULL,
    revoked_at_utc timestamptz,
    updated_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (operator_tenant_id, operator_object_id, session_id),
    UNIQUE (session_id)
);

CREATE INDEX bff_auth_sessions_expiry_idx ON platform.bff_auth_sessions (expires_at_utc);
CREATE INDEX oidc_transactions_expiry_idx ON platform.oidc_transactions (expires_at_utc);

CREATE FUNCTION platform.protect_bff_session_revocation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.revoked_at_utc IS NOT NULL AND NEW.revoked_at_utc IS DISTINCT FROM OLD.revoked_at_utc THEN
        RAISE EXCEPTION 'a revoked BFF session cannot be reactivated';
    END IF;
    IF OLD.revoked_at_utc IS NOT NULL AND
       (NEW.ciphertext IS DISTINCT FROM OLD.ciphertext OR NEW.nonce IS DISTINCT FROM OLD.nonce OR
        NEW.authentication_tag IS DISTINCT FROM OLD.authentication_tag OR
        NEW.wrapped_data_key IS DISTINCT FROM OLD.wrapped_data_key OR NEW.key_id IS DISTINCT FROM OLD.key_id OR
        NEW.key_version IS DISTINCT FROM OLD.key_version OR NEW.expires_at_utc IS DISTINCT FROM OLD.expires_at_utc) THEN
        RAISE EXCEPTION 'revoked BFF session material is immutable';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER bff_auth_sessions_revoke_once
    BEFORE UPDATE ON platform.bff_auth_sessions
    FOR EACH ROW EXECUTE FUNCTION platform.protect_bff_session_revocation();

CREATE FUNCTION platform.protect_oidc_transaction_consumption() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.consumed_at_utc IS NOT NULL AND NEW.consumed_at_utc IS DISTINCT FROM OLD.consumed_at_utc THEN
        RAISE EXCEPTION 'an OIDC transaction cannot be replayed';
    END IF;
    IF NEW.transaction_id IS DISTINCT FROM OLD.transaction_id OR NEW.state_sha256 IS DISTINCT FROM OLD.state_sha256 OR
       NEW.ciphertext IS DISTINCT FROM OLD.ciphertext OR NEW.nonce IS DISTINCT FROM OLD.nonce OR
       NEW.authentication_tag IS DISTINCT FROM OLD.authentication_tag OR NEW.wrapped_data_key IS DISTINCT FROM OLD.wrapped_data_key OR
       NEW.key_id IS DISTINCT FROM OLD.key_id OR NEW.key_version IS DISTINCT FROM OLD.key_version OR
       NEW.expires_at_utc IS DISTINCT FROM OLD.expires_at_utc THEN
        RAISE EXCEPTION 'OIDC transaction payload is immutable';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER oidc_transaction_consume_once
    BEFORE UPDATE ON platform.oidc_transactions
    FOR EACH ROW EXECUTE FUNCTION platform.protect_oidc_transaction_consumption();

ALTER TABLE platform.oidc_transactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.oidc_transactions FORCE ROW LEVEL SECURITY;
CREATE POLICY oidc_transaction_bff_access ON platform.oidc_transactions
    USING (current_user = 'platform_app')
    WITH CHECK (current_user = 'platform_app');

ALTER TABLE platform.bff_auth_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.bff_auth_sessions FORCE ROW LEVEL SECURITY;
CREATE POLICY bff_auth_session_operator_isolation ON platform.bff_auth_sessions
    USING (operator_tenant_id = NULLIF(current_setting('platform.operator_tenant_id', true), '')::uuid
       AND operator_object_id = NULLIF(current_setting('platform.operator_object_id', true), '')::uuid)
    WITH CHECK (operator_tenant_id = NULLIF(current_setting('platform.operator_tenant_id', true), '')::uuid
            AND operator_object_id = NULLIF(current_setting('platform.operator_object_id', true), '')::uuid);

CREATE FUNCTION platform.resolve_bff_session(session_id uuid)
RETURNS TABLE (operator_tenant_id uuid, operator_object_id uuid)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, platform
AS $$
        SELECT session.operator_tenant_id, session.operator_object_id
        FROM platform.bff_auth_sessions AS session
        WHERE session.session_id = $1
            AND session.revoked_at_utc IS NULL
            AND session.expires_at_utc > clock_timestamp()
$$;
REVOKE ALL ON FUNCTION platform.resolve_bff_session(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.resolve_bff_session(uuid) TO platform_app;

GRANT SELECT, INSERT, UPDATE ON platform.oidc_transactions TO platform_app;
GRANT SELECT, INSERT, UPDATE ON platform.bff_auth_sessions TO platform_app;
REVOKE DELETE, TRUNCATE ON platform.oidc_transactions, platform.bff_auth_sessions FROM platform_app;

COMMIT;