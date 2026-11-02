using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using M365.SecurityAssessment.Authentication;
using M365.SecurityAssessment.Persistence;

namespace M365.SecurityAssessment.Api;

public static class PlatformDataEndpoints
{
    public static IEndpointRouteBuilder MapPlatformDataEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool evidenceStoreConfigured)
    {
        var group = endpoints.MapGroup("/platform/organizations/{organizationId:guid}/customers/{customerTenantId:guid}");

        group.MapGet("/assessments", async (HttpContext context, IMetadataRepository repository) =>
        {
            var assignment = PlatformAuthorizationContext.GetAssignment(context);
            if (assignment is null) return Results.Forbid();

            var records = await repository.ListAsync(
                assignment.Scope, MetadataRecordKind.Assessment, 100, context.RequestAborted);
            return Results.Ok(records);
        }).RequireAuthorization("Platform.Viewer");

        group.MapGet("/assessments/{assessmentId:guid}/versions/{version:int}", async (
            HttpContext context, Guid assessmentId, int version, IMetadataRepository repository) =>
        {
            var assignment = PlatformAuthorizationContext.GetAssignment(context);
            if (assignment is null) return Results.Forbid();

            var record = await repository.GetAsync(
                assignment.Scope, MetadataRecordKind.Assessment, assessmentId, version, context.RequestAborted);
            return record is null ? Results.NotFound() : Results.Ok(record);
        }).RequireAuthorization("Platform.Viewer");

        group.MapGet("/audit", async (HttpContext context, IAuditLedgerRepository repository) =>
        {
            var assignment = PlatformAuthorizationContext.GetAssignment(context);
            if (assignment is null) return Results.Forbid();

            var query = context.Request.Query;
            if (!TryReadUtc(query["fromUtc"].ToString(), out var fromUtc) ||
                !TryReadUtc(query["toUtc"].ToString(), out var toUtc) ||
                !TryReadUtc(query["beforeUtc"].ToString(), out var beforeUtc) ||
                (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc))
            {
                return Results.BadRequest(new { error = "invalid_audit_time_filter" });
            }

            AuditOutcome? outcome = null;
            var outcomeText = query["outcome"].ToString();
            if (outcomeText.Length > 0)
            {
                if (!Enum.TryParse<AuditOutcome>(outcomeText, true, out var parsedOutcome) ||
                    !Enum.IsDefined(parsedOutcome))
                {
                    return Results.BadRequest(new { error = "invalid_audit_outcome" });
                }

                outcome = parsedOutcome;
            }

            var limit = 100;
            var limitText = query["limit"].ToString();
            if (limitText.Length > 0 && (!int.TryParse(limitText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 500))
            {
                return Results.BadRequest(new { error = "invalid_audit_limit" });
            }

            var filter = new AuditQueryFilter(
                fromUtc,
                toUtc,
                query["userId"].ToString() is { Length: > 0 } userId ? userId : null,
                query["service"].ToString() is { Length: > 0 } service ? service : null,
                query["setting"].ToString() is { Length: > 0 } setting ? setting : null,
                query["resource"].ToString() is { Length: > 0 } resource ? resource : null,
                outcome,
                limit,
                beforeUtc);
            var events = await repository.QueryAsync(assignment.Scope, filter, context.RequestAborted);
            return Results.Ok(events);
        }).RequireAuthorization("Platform.Viewer");

        if (evidenceStoreConfigured)
        {
            group.MapGet("/evidence/{evidenceId:guid}", async (
                HttpContext context, Guid evidenceId, IEvidenceRepository repository) =>
            {
                var assignment = PlatformAuthorizationContext.GetAssignment(context);
                if (assignment is null) return Results.Forbid();

                try
                {
                    var stream = await repository.OpenVerifiedReadAsync(
                        assignment.Scope, evidenceId, context.RequestAborted);
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    return Results.File(stream, "application/octet-stream", $"{evidenceId:D}.bin");
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidDataException)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Evidence integrity validation failed.");
                }
            }).RequireAuthorization("Platform.Viewer");
        }
        else
        {
            group.MapGet("/evidence/{evidenceId:guid}", () =>
                Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
                .RequireAuthorization("Platform.Viewer");
        }

        return endpoints;
    }

    private static bool TryReadUtc(string value, out DateTimeOffset? result)
    {
        result = null;
        if (value.Length == 0) return true;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }
}