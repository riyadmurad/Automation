# Persistence Foundation

This project contains the PostgreSQL metadata/session/audit repositories and evidence storage providers. It is not wired into the API yet; the API/auth owners must register it and build authorized routes around the repositories. Do not mark the related OpenSpec tasks complete until those owning integrations and acceptance criteria pass.

## Local PostgreSQL

Use Docker Desktop with the Linux engine. Set `POSTGRES_ADMIN_PASSWORD` in the shell or an approved local secret manager, then run `docker compose -f infra/postgres/compose.yaml up -d` from the platform directory. The compose service binds only to loopback. It contains no default password and is intended for local development only.

Create the application role as a non-owner, non-superuser role from the PostgreSQL administrator session, then set its password using psql's interactive `\password platform_app` prompt. Do not place that password in source, shell history, compose files, or logs. Apply schema changes explicitly, as an administrator, with `psql -v ON_ERROR_STOP=1 -f /migrations/001_storage_audit.sql`. The migration is never run automatically by application startup.

Supply the application connection as `ConnectionStrings__PlatformPersistence` (or `PlatformPersistence__ConnectionString`) to the ASP.NET process. It must use `platform_app`, not the database owner. The database role gets only scoped read/insert grants, session updates for key rotation and revocation, and no update/delete/truncate grant on metadata, evidence references, or audit history. Provision customer scope rows through a separately authorized control-plane path; do not broaden the application role.

Register the database repositories in the API startup with `builder.Services.AddPlatformPersistence(builder.Configuration);`. This registration fails when the connection string is absent and does not apply schema changes. Register exactly one evidence provider separately: `AddLocalEvidenceStore(configuration, rootDirectory)` for explicit synthetic-local Development/Testing, or `AddAzureEvidenceStore(privateContainerClient)` for a configured private container. The auth owner must provide an `ISessionEnvelopeProtector`; no default key or plaintext fallback is registered.

## Local Evidence

Development evidence can use `AddLocalEvidenceStore(configuration, rootDirectory)` with a dedicated local directory. Registration requires Development or Testing and `PlatformPersistence:EvidenceMode=synthetic-local`; it cannot be selected in production configuration. This provider does not encrypt files itself. It writes immutable organization/tenant-prefixed objects, enforces a 64 MiB limit, checks the supplied SHA-256 before returning success, and rechecks the digest before returning any stream. Keep real customer evidence out of this provider.

For hosted evidence, the API owner may create an `AzureEvidenceBlobStore` with a private `BlobContainerClient` authorized by managed identity. The object key is derived from organization, customer tenant, evidence ID, and content hash; writes are conditional and never overwrite an existing object. Configure private networking, least-privilege container access, encryption at rest (including any approved customer-managed key policy), retention, and audit at the Azure resource boundary. Retrieval must go through `IEvidenceRepository`, which first authorizes the scoped metadata row and then revalidates the object hash. Do not expose a blob URL or SAS token to the browser.

## Sessions and Audit

Session persistence accepts only ciphertext, nonce, authentication tag, wrapped data key, key ID/version, expiry, and immutable user/tenant/session identifiers. An `ISessionEnvelopeProtector` implementation must be supplied by the authentication owner; this project intentionally does not invent an encryption key source or claim that a Key Vault integration exists. Revocation is one-way for each session ID; a fresh sign-in uses a new ID. Missing or retired decryption keys must fail closed in the caller.

Audit append requires trusted requester/approver/executor actor references from validated authentication/authorization context. Do not construct them from request display names. The database assigns the UTC event time, redacts sensitive values before persistence, and hashes events in append order per organization/tenant. `AuditExportFormatter` only formats repository results; API authorization, filter-to-customer assignment, reauthorization at download, and retention enforcement remain API/operations responsibilities. A setting write must not start until its durable intent append has completed successfully.

Each setting in a multi-setting operation gets its own event and shares the operation/correlation identifiers. `previousValue`, `requestedValue`, and `verifiedValue` are separate typed values; known JSON `null`, absent, unknown, and redacted are distinct. Actor roles are also separate: requester and optional approver are users, executor is the executing workload, and scheduled/external/unknown observations use explicit actor kinds rather than an invented user.

Typical event progression is append-only:

| Event                        | Outcome                                  | Previous                            | Requested                  | Verified                                |
| ---------------------------- | ---------------------------------------- | ----------------------------------- | -------------------------- | --------------------------------------- |
| Intent                       | `requested`                              | the pre-write observed value        | the approved setting value | `unknown`                               |
| Result after a lost response | `unknown`                                | the same pre-write value            | the same requested value   | `absent` or `unknown`                   |
| Reconciliation               | `verified`, `failed`, or still `unknown` | the newly observed value when known | `unknown` (no new write)   | the separately observed effective value |

Reconciliation appends a new event using the original operation correlation; it never edits the intent or changes an unknown result into a success. Verification after a known service response is likewise a distinct event and records the post-write observed value rather than copying the requested value.

## Tests

Run the real PostgreSQL integration suite from the workspace root with:

```powershell
& 'C:\Users\DEV\AppData\Local\Microsoft\dotnet\dotnet.exe' test .\m365-azure-security-assessment-platform\tests\backend\M365.SecurityAssessment.Persistence.Tests\M365.SecurityAssessment.Persistence.Tests.csproj
```

The tests start a disposable Linux PostgreSQL container, generate an ephemeral strong password in memory, create a non-owner application role, apply the schema, and dispose the container. They do not connect to Azure or customer tenants. The explicit `infra/postgres` compose service is separate from the test lifecycle.