# Platform Authentication Integration

This project owns API token validation, customer assignment policies, purpose-specific connection checks, and Key Vault-backed session-envelope protection. It is intentionally not wired into the shared API `Program.cs`; the API owner integrates the public extensions below.

## API Registration

Register PostgreSQL persistence first, then authentication. Middleware must run before mapped endpoints.

```csharp
builder.Services.AddPlatformPersistence(builder.Configuration);
builder.Services.AddPlatformAuthentication(builder.Configuration);

var app = builder.Build();
app.UsePlatformAuthentication();
app.MapPlatformAuthentication();
```

Customer endpoints must include both the organization and customer tenant in the route and require one of `Platform.Viewer`, `Platform.Assessor`, `Platform.Approver`, `Platform.Operator`, or `Platform.Admin`. Assignment lookup uses validated `oid` and `tid` token claims plus both route identifiers. Request headers, email addresses, display names, and a tenant ID alone never establish an assignment.

`CustomerConnectionAuthorizationService.CheckAsync` must run again at operation admission and before each privileged call. Assessment and remediation are separate rows and purposes. The application stores only a credential reference and verified granted-scope metadata; the reference is never returned by status routes. Consent and credential provisioning remain a separately authorized control-plane task. No live connector, credential, permission grant, or customer authorization is created by this project.

`PlatformApprovalAuthorization.CanApprove` enforces independent approval using the active assignment's customer policy and immutable operator object IDs. Approval callers must still bind the approver to the immutable plan revision and persist the attempt in the audit ledger.

## Required Runtime Configuration

`AddPlatformAuthentication` fails at API startup when trusted tenants, audiences, issuer, standalone binding, persistence registration, or Key Vault key-version configuration is missing or malformed. Values below are placeholders, not approved production settings.

```json
{
  "PlatformAuthentication": {
    "DeploymentMode": "Msp",
    "IssuerBaseUrl": "https://login.microsoftonline.com/",
    "ApiClientId": "<api-app-client-id>",
    "ValidAudiences": [ "api://<api-app-client-id>" ],
    "TrustedPlatformTenants": [ "<operator-tenant-guid>" ],
    "KeyVault": {
      "VaultUri": "<approved-key-vault-uri>",
      "KeyName": "operator-session",
      "CurrentVersion": 2,
      "Versions": {
        "1": "<fully-versioned-prior-key-id>",
        "2": "<fully-versioned-current-key-id>"
      }
    }
  }
}
```

Standalone mode additionally requires `StandaloneCustomerTenantId` and rejects API tokens from any other tenant. The API validates the exact v2 issuer for the trusted tenant, configured API audience, signed token, expiry, `tid`, and immutable `oid`. Token signing keys are resolved by Microsoft.Identity.Web from Microsoft identity metadata; no signing secret is embedded in the application.

The API managed identity needs only the configured Key Vault key `wrapKey` and `unwrapKey` operations. Use a versioned RSA key supporting RSA-OAEP-256. Keep old key IDs in `KeyVault:Versions` while envelopes need them; remove an old version only after planned re-encryption or safe session invalidation. Missing keys and Key Vault failures fail closed. No key material or plaintext session cache is logged.

## Database Migration

Apply migrations explicitly as an administrator after the persistence foundation migration; application startup never migrates the database.

1. `M365.SecurityAssessment.Persistence/Migrations/001_storage_audit.sql`
2. `Migrations/001_platform_authentication.sql`
3. `Migrations/002_bff_auth.sql`

The application role receives scoped reads for assignments and connection metadata, no assignment/connection provisioning rights, and scoped session updates for rotation/revocation. Provision or revoke assignments and connection references through a separately authorized control-plane path. Never grant the application role owner or `BYPASSRLS` privileges.

## Next.js BFF Configuration

The web server requires `PLATFORM_WEB_ORIGIN`, `PLATFORM_API_ORIGIN`, `PLATFORM_ENTRA_CLIENT_ID`, `PLATFORM_ENTRA_CLIENT_SECRET`, `PLATFORM_TRUSTED_TENANTS`, `PLATFORM_API_SCOPE`, `PLATFORM_AUTH_DATABASE_URL`, `PLATFORM_KEY_VAULT_URL`, `PLATFORM_SESSION_KEY_NAME`, `PLATFORM_SESSION_KEY_VERSION`, and `PLATFORM_SESSION_KEY_VERSIONS`. Production origins must be HTTPS. `PLATFORM_ENTRA_CLIENT_SECRET` and the database URL belong in the deployment secret provider, never browser-exposed `NEXT_PUBLIC_*` variables, source, or logs. Production Key Vault calls use managed identity; local tests inject synthetic key wrappers and never connect to customer tenants.

The BFF stores PKCE verifier, nonce, and return path in short-lived encrypted PostgreSQL transactions. State is single-use, callback nonce is compared after MSAL validation, and local sessions store only encrypted MSAL cache ciphertext with wrapped data keys and explicit key versions. The browser receives only an opaque random session ID in a `Secure; HttpOnly; SameSite=Lax` cookie; tenant and immutable user IDs are resolved from the active database session, never from cookie-supplied identity fields. BFF mutations require an exact configured `Origin`; API forwarding uses a fixed origin and API-audience token acquired server-side. The API remains authoritative for every organization/customer role check.

The web and API use the same PostgreSQL service but distinct BFF and platform session tables. The API's `ISessionEnvelopeProtector` is registered for the existing encrypted-session repository; this slice does not claim the Next BFF cache is stored through that C# repository. Both providers use the same versioned Key Vault envelope model and tenant/user-bound authenticated data.