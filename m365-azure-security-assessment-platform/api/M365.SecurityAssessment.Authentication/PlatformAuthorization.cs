using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication;

public enum PlatformRole
{
    Viewer,
    Assessor,
    Approver,
    Operator,
    Admin
}

public enum ConnectionPurpose
{
    Assessment,
    Remediation
}

public sealed record PlatformCustomerAssignment(
    CustomerDataScope Scope,
    Guid OperatorTenantId,
    Guid OperatorObjectId,
    IReadOnlySet<PlatformRole> Roles,
    bool RequiresIndependentApproval);

public sealed record CustomerConnectionAuthorization(
    CustomerDataScope Scope,
    ConnectionPurpose Purpose,
    IReadOnlySet<string> GrantedScopes,
    bool IsActive,
    string Health,
    DateTimeOffset? LastCheckedAtUtc);

public sealed record ConnectionPermissionResult(
    bool IsAuthorized,
    IReadOnlyList<string> MissingScopes,
    string Reason);

public interface ISessionEnvelopeKeyRotator
{
    Task<EncryptedSessionEnvelope> RewrapDataKeyAsync(
        EncryptedSessionEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public sealed class SessionEnvelopeRotationService(
    IEncryptedSessionRepository repository,
    ISessionEnvelopeKeyRotator rotator)
{
    public async Task<bool> RewrapActiveSessionAsync(
        CustomerDataScope scope,
        Guid userObjectId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await repository.GetActiveAsync(scope, userObjectId, sessionId, cancellationToken);
        if (envelope is null) return false;
        var rewrapped = await rotator.RewrapDataKeyAsync(envelope, cancellationToken);
        if (rewrapped.KeyVersion == envelope.KeyVersion && rewrapped.KeyId == envelope.KeyId) return false;
        await repository.StoreAsync(rewrapped, cancellationToken);
        return true;
    }
}

public interface ICustomerAuthorizationRepository
{
    Task<PlatformCustomerAssignment?> GetAssignmentAsync(
        Guid operatorTenantId,
        Guid operatorObjectId,
        Guid organizationId,
        Guid customerTenantId,
        CancellationToken cancellationToken = default);

    Task<CustomerConnectionAuthorization?> GetConnectionAsync(
        PlatformCustomerAssignment assignment,
        ConnectionPurpose purpose,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerRoleRequirement(IReadOnlySet<PlatformRole> acceptedRoles) : IAuthorizationRequirement
{
    public IReadOnlySet<PlatformRole> AcceptedRoles { get; } = acceptedRoles;
}

public sealed class CustomerRoleAuthorizationHandler(
    ICustomerAuthorizationRepository repository,
    IHttpContextAccessor httpContextAccessor,
    PlatformAuthenticationOptions options) : AuthorizationHandler<CustomerRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerRoleRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var oidText = context.User.FindFirstValue("oid");
        var tidText = context.User.FindFirstValue("tid");
        var organizationText = httpContext?.GetRouteValue("organizationId")?.ToString();
        var customerText = httpContext?.GetRouteValue("customerTenantId")?.ToString();
        if (httpContext is null || !Guid.TryParse(oidText, out var operatorObjectId) ||
            !Guid.TryParse(tidText, out var operatorTenantId) ||
            !Guid.TryParse(organizationText, out var organizationId) ||
            !Guid.TryParse(customerText, out var customerTenantId) ||
            (options.DeploymentMode == PlatformDeploymentMode.Standalone &&
             options.StandaloneCustomerTenantId != customerTenantId))
        {
            return;
        }

        var assignment = await repository.GetAssignmentAsync(
            operatorTenantId, operatorObjectId, organizationId, customerTenantId, httpContext.RequestAborted);
        if (assignment is null || !assignment.Roles.Overlaps(requirement.AcceptedRoles))
        {
            return;
        }

        httpContext.Items[PlatformAuthorizationContext.ItemKey] = assignment;
        context.Succeed(requirement);
    }
}

public static class PlatformAuthorizationContext
{
    public const string ItemKey = "M365.SecurityAssessment.CustomerAssignment";

    public static PlatformCustomerAssignment? GetAssignment(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as PlatformCustomerAssignment : null;
}

public static class PlatformApprovalAuthorization
{
    public static bool CanApprove(
        PlatformCustomerAssignment assignment,
        Guid proposerObjectId,
        Guid approverObjectId)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (approverObjectId == Guid.Empty || approverObjectId != assignment.OperatorObjectId || proposerObjectId == Guid.Empty)
        {
            return false;
        }

        var canApprove = assignment.Roles.Contains(PlatformRole.Approver) || assignment.Roles.Contains(PlatformRole.Admin);
        return canApprove && (!assignment.RequiresIndependentApproval || proposerObjectId != approverObjectId);
    }
}

public sealed class CustomerConnectionAuthorizationService(ICustomerAuthorizationRepository repository)
{
    public Task<CustomerConnectionAuthorization?> GetStatusAsync(
        PlatformCustomerAssignment assignment,
        ConnectionPurpose purpose,
        CancellationToken cancellationToken = default) =>
        repository.GetConnectionAsync(assignment, purpose, cancellationToken);

    public async Task<ConnectionPermissionResult> CheckAsync(
        PlatformCustomerAssignment assignment,
        ConnectionPurpose purpose,
        IEnumerable<string> requiredScopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var roleAuthorized = purpose switch
        {
            ConnectionPurpose.Assessment => assignment.Roles.Overlaps(
                [PlatformRole.Assessor, PlatformRole.Operator, PlatformRole.Admin]),
            ConnectionPurpose.Remediation => assignment.Roles.Overlaps([PlatformRole.Operator, PlatformRole.Admin]),
            _ => false
        };
        if (!roleAuthorized)
        {
            return new ConnectionPermissionResult(false, [], "The operator role is not authorized for this connection purpose.");
        }

        var connection = await repository.GetConnectionAsync(assignment, purpose, cancellationToken);
        if (connection is null || !connection.IsActive)
        {
            return new ConnectionPermissionResult(false, [], "The customer connection is missing, revoked, or unhealthy.");
        }

        if (!string.Equals(connection.Health, "healthy", StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectionPermissionResult(false, [], "The customer connection health is not verified as healthy.");
        }

        var missing = requiredScopes.Distinct(StringComparer.Ordinal)
            .Where(scope => !connection.GrantedScopes.Contains(scope))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return missing.Length == 0
            ? new ConnectionPermissionResult(true, [], "Authorized for the requested purpose.")
            : new ConnectionPermissionResult(false, missing, "Customer consent is missing required permissions.");
    }
}