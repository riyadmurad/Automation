CREATE ROLE platform_app
    NOLOGIN
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOINHERIT
    NOBYPASSRLS;

SELECT * FROM pgaadauth_create_principal(:'api_identity_name', false, false);
GRANT platform_app TO :"api_identity_name";