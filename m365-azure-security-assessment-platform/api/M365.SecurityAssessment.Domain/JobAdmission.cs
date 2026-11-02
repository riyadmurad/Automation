namespace M365.SecurityAssessment.Domain;

public enum JobPurpose { Assessment, Remediation }

public sealed record StoredJob(Guid JobId, Guid OrganizationId, Guid TenantId, JobPurpose Purpose,
    string ConnectionId, bool RequestsWrite, string CredentialReferenceId);
public sealed record JobEnvelope(string ContractVersion, Guid JobId, Guid OrganizationId, Guid TenantId, JobPurpose Purpose, string ConnectionId);
public sealed record WorkerBinding(Guid OrganizationId, Guid TenantId, JobPurpose Purpose);
public sealed record ConnectionBinding(string ConnectionId, Guid OrganizationId, Guid TenantId,
    JobPurpose Purpose, string CredentialReferenceId, JobPurpose CredentialPurpose, bool IsActive);
public sealed record JobAdmissionDecision(bool Admitted, string ReasonCode);

public static class JobAdmission
{
    public static JobAdmissionDecision Admit(
        StoredJob storedJob, JobEnvelope envelope, WorkerBinding worker, ConnectionBinding connection, bool writesEnabled)
    {
        if (!Enum.IsDefined(storedJob.Purpose) || !Enum.IsDefined(envelope.Purpose) ||
            !Enum.IsDefined(worker.Purpose) || !Enum.IsDefined(connection.Purpose) ||
            !Enum.IsDefined(connection.CredentialPurpose))
        {
            return new(false, "invalid-enum-value");
        }

        if (storedJob.JobId == Guid.Empty || storedJob.OrganizationId == Guid.Empty || storedJob.TenantId == Guid.Empty ||
            worker.OrganizationId == Guid.Empty || worker.TenantId == Guid.Empty || connection.OrganizationId == Guid.Empty ||
            connection.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(storedJob.ConnectionId) ||
            string.IsNullOrWhiteSpace(storedJob.CredentialReferenceId))
        {
            return new(false, "incomplete-binding");
        }

        if (envelope.ContractVersion != ContractVersions.JobEnvelope)
        {
            return new(false, "unsupported-contract-version");
        }

        if (storedJob.JobId != envelope.JobId || storedJob.OrganizationId != envelope.OrganizationId ||
            storedJob.TenantId != envelope.TenantId || storedJob.Purpose != envelope.Purpose ||
            !string.Equals(storedJob.ConnectionId, envelope.ConnectionId, StringComparison.Ordinal))
        {
            return new(false, "queue-envelope-mismatch");
        }

        if (storedJob.OrganizationId != worker.OrganizationId || storedJob.OrganizationId != connection.OrganizationId)
        {
            return new(false, "organization-binding-mismatch");
        }

        if (storedJob.TenantId != worker.TenantId || storedJob.TenantId != connection.TenantId)
        {
            return new(false, "tenant-binding-mismatch");
        }

        if (storedJob.Purpose != worker.Purpose || storedJob.Purpose != connection.Purpose)
        {
            return new(false, "purpose-binding-mismatch");
        }

        if (!connection.IsActive || !string.Equals(storedJob.ConnectionId, connection.ConnectionId, StringComparison.Ordinal))
        {
            return new(false, "connection-inactive-or-mismatched");
        }

        if (!string.Equals(storedJob.CredentialReferenceId, connection.CredentialReferenceId, StringComparison.Ordinal))
        {
            return new(false, "credential-reference-mismatch");
        }

        if (storedJob.Purpose != connection.CredentialPurpose)
        {
            return new(false, "credential-purpose-mismatch");
        }

        if (storedJob.RequestsWrite && (!writesEnabled || storedJob.Purpose != JobPurpose.Remediation))
        {
            return new(false, "writes-disabled");
        }

        return new(true, "admitted-for-bound-purpose");
    }
}