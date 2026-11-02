using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Authentication.Tests;

public sealed class CustomerRoleAuthorizationHandlerTests
{
    [Fact]
    public async Task DirectApiPolicyDeniesViewerAndIgnoresCallerSuppliedIdentityHeaders()
    {
        var scope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantB);
        var assignment = new PlatformCustomerAssignment(
            scope,
            AuthTestData.TenantA,
            AuthTestData.UserA,
            new HashSet<PlatformRole> { PlatformRole.Viewer },
            true);
        var repository = new StubAuthorizationRepository(assignment);
        var request = new DefaultHttpContext();
        request.Request.RouteValues["organizationId"] = scope.OrganizationId.ToString("D");
        request.Request.RouteValues["customerTenantId"] = scope.CustomerTenantId.ToString("D");
        request.Request.Headers["X-User-Object-Id"] = AuthTestData.UserA.ToString("D");
        request.User = Principal(AuthTestData.UserA, AuthTestData.TenantA);
        var options = AuthTestData.Options();
        var handler = new CustomerRoleAuthorizationHandler(
            repository,
            new HttpContextAccessor { HttpContext = request },
            options);
        var operatorRequirement = new CustomerRoleRequirement(new HashSet<PlatformRole> { PlatformRole.Operator });
        var viewerRequirement = new CustomerRoleRequirement(new HashSet<PlatformRole> { PlatformRole.Viewer });

        var operatorContext = new AuthorizationHandlerContext([operatorRequirement], request.User, request);
        await handler.HandleAsync(operatorContext);
        var viewerContext = new AuthorizationHandlerContext([viewerRequirement], request.User, request);
        await handler.HandleAsync(viewerContext);

        Assert.False(operatorContext.HasSucceeded);
        Assert.True(viewerContext.HasSucceeded);
        Assert.Equal(AuthTestData.UserA, repository.LastOperatorObjectId);
    }

    [Fact]
    public async Task AssignmentForAnotherCustomerCannotAuthorizeDirectApiRequest()
    {
        var assignedScope = new CustomerDataScope(Guid.NewGuid(), AuthTestData.TenantB);
        var assignment = new PlatformCustomerAssignment(
            assignedScope,
            AuthTestData.TenantA,
            AuthTestData.UserA,
            new HashSet<PlatformRole> { PlatformRole.Operator },
            true);
        var repository = new StubAuthorizationRepository(assignment);
        var request = new DefaultHttpContext();
        request.Request.RouteValues["organizationId"] = assignedScope.OrganizationId.ToString("D");
        request.Request.RouteValues["customerTenantId"] = AuthTestData.TenantA.ToString("D");
        request.User = Principal(AuthTestData.UserA, AuthTestData.TenantA);
        var handler = new CustomerRoleAuthorizationHandler(
            repository,
            new HttpContextAccessor { HttpContext = request },
            AuthTestData.Options());
        var requirement = new CustomerRoleRequirement(new HashSet<PlatformRole> { PlatformRole.Operator });
        var context = new AuthorizationHandlerContext([requirement], request.User, request);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static ClaimsPrincipal Principal(Guid objectId, Guid tenantId) => new(
        new ClaimsIdentity(
        [
            new Claim("oid", objectId.ToString("D")),
            new Claim("tid", tenantId.ToString("D"))
        ], "test"));

    private sealed class StubAuthorizationRepository(PlatformCustomerAssignment assignment) : ICustomerAuthorizationRepository
    {
        public Guid LastOperatorObjectId { get; private set; }

        public Task<PlatformCustomerAssignment?> GetAssignmentAsync(
            Guid operatorTenantId,
            Guid operatorObjectId,
            Guid organizationId,
            Guid customerTenantId,
            CancellationToken cancellationToken = default)
        {
            LastOperatorObjectId = operatorObjectId;
            return Task.FromResult<PlatformCustomerAssignment?>(
                operatorTenantId == assignment.OperatorTenantId && operatorObjectId == assignment.OperatorObjectId &&
                organizationId == assignment.Scope.OrganizationId && customerTenantId == assignment.Scope.CustomerTenantId
                    ? assignment
                    : null);
        }

        public Task<CustomerConnectionAuthorization?> GetConnectionAsync(
            PlatformCustomerAssignment assignment,
            ConnectionPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CustomerConnectionAuthorization?>(null);
    }
}