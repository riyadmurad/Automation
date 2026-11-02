# Infrastructure Operations Candidate

This is an un-deployed Bicep candidate for central MSP or single-customer standalone hosting. It creates a separate public web perimeter and internal API environment, private PostgreSQL subnet, private endpoints for Blob, Key Vault and Service Bus, private DNS links, a managed-identity role map, and monitoring. Assessment/remediation worker modules, per-tenant queues, identities, credential grants, and evidence-container access are conditionally created only for customer bindings carrying an immutable adapter-manifest digest and a passing external approval record. The empty binding list provisions zero workers. No Windows host is implemented; any Windows-only adapter manifest is blocked until a separately approved isolated VM design exists. No Azure resources or customer tenant connections were created or queried for this work.

## Preflight

All resource names, region, network prefixes, customer/worker bindings, service SKUs, capacity, retention, replica limits, and container CPU/memory settings are inputs; the Bicep file does not select production values. Build a deployment manifest from the actual reviewed inputs and use `infra/validation/validate_deployment.py` before any separately authorized deployment. The production template at `production-approval.template.json` is intentionally invalid until completed with approval evidence and immutable image references. Adapter-manifest approval records must correspond to independently reviewed immutable manifests; the validator checks the recorded digest/scope fields but does not inspect adapter code, authenticate approvers, or verify an external approval system. An authorized release reviewer must validate those records independently.

For `pre-production` and `production`, the manifest must identify an approved network profile, reachable firewall address, and egress-policy reference. The template routes ACA subnets through that supplied firewall address but does not create or peer a hub firewall and does not install its FQDN policy. The firewall owner must verify the route, return path, DNS forwarding, required Microsoft API destinations, image registry access, and failure behavior before the profile is approved. Private endpoints do not make Microsoft Graph or other Microsoft public APIs private.

PostgreSQL Flexible Server uses a delegated private subnet and private DNS; public network access and password authentication are disabled. A pre-existing, separately authorized Entra migration principal is the PostgreSQL server administrator; its immutable object ID and display name are required deployment inputs. Run `postgres/bootstrap-application-role.sql` only through a separately authorized migration process using that principal, bind its `api_identity_name` variable to the API managed identity, then apply the application migrations. The API connects through the non-login `platform_app` role; the API, assessment, and remediation identities are distinct. Assessment and remediation workers receive credential-secret read only on caller-supplied tenant-specific secret scopes for their own purpose. Those secret resources must already be present; this infrastructure candidate neither creates connector credentials nor grants consent or customer-service permissions.

The worker images currently start disabled/idle and have no verified service adapters. This template is not evidence that a worker can process a job, that application database authentication is wired, or that a deployment is production-ready. Image references must resolve from a registry reachable by the Container Apps environments; this template deliberately stores no registry password and does not provision registry access.

## Static Checks

From the platform project directory, run the offline tests and build the template with the pinned Bicep CLI. These checks do not contact Azure and are not a what-if or deployment authorization:

```text
python -m unittest discover -s tests/infra -v
python infra/validation/validate_deployment.py infra/validation/production-approval.template.json
bicep build infra/azure/main.bicep --stdout
```

The production-template validation command is expected to reject the unfilled template. Do not treat successful Bicep compilation as proof that a region, SKU, role assignment, network route, capacity, price, retention policy, or Azure API operation has been approved or provisioned.

## Premium Capacity and Cost Review

`cost-estimate.template.json` intentionally contains null workload and price values. It is not a pricing quote. Before production, populate it from an approved workload profile and an independently reviewed Azure pricing estimate, including the Service Bus Premium messaging-unit count, app replica/CPU/memory limits, PostgreSQL SKU/storage/HA, region, network path, and data-retention periods. Record the estimate source and timestamp, budget currency/limit, approver, and approval reference in the deployment manifest. The offline checks reject a production Service Bus tier other than Premium and reject missing cost, budget, retention, region, network, or production-SLO records.

The separate development/test defaults in `infra/validation/development-test-defaults.json` are synthetic-only proposals. They are never substituted for production SLOs, budget limits, or customer data retention.

## Database Compatibility and Recovery Drill

The checked-in compatibility baseline names the current migration and `JobEnvelope` version only. There is no prior supported release baseline in this repository, so compatibility with a prior release remains unverified. CI applies the current migration to an empty real PostgreSQL container and runs current job-contract rejection tests; this is not a prior-release expand/contract certification. Do not add a destructive contract migration until a named prior app/worker/schema version and compatibility fixture exist.

For a recovery exercise, use an isolated environment with customer execution disabled, worker replicas at zero, no connector-secret role assignments, and no outbound customer API path. Restore data and keys only under the approved retention policy; reconcile revocations, tenant bindings, job ownership, approvals, and external operation outcomes before restoring any execution access. Application rollback preserves the forward-compatible schema and audit/evidence history; this candidate has no automatic database downgrade and never reverses customer settings.

## Incident Stop

1. Stop admission of new jobs in the application control plane and record the incident/operator context.
2. Scale assessment and remediation workers to zero and remove their queue receiver and secret-scope assignments if isolation is in doubt.
3. Preserve in-flight operation IDs and audit intent; reconcile uncertain remote outcomes before retrying. Do not describe a stop as reversing an already-applied customer change.
4. Keep web/API access read-only or disabled according to the incident plan. Preserve audit archives and evidence under their approved retention/legal-hold policy.
5. Re-enable execution only after authorization, queue, key, network, image digest, schema/job compatibility, and emergency-access checks have been reviewed by the designated approvers.

## Separate Deployment Authorization

No deployment command is included or run by this candidate. Any future deployment requires an independently approved region, network/egress profile, Premium capacity and cost, budget, retention, production SLOs, image registry access, database role bootstrap, and recovery procedure. No live Microsoft Graph/customer test is part of CI.