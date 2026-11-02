using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M365.SecurityAssessment.Persistence;

public static class AuditExportFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly string[] CsvHeaders =
    [
        "eventId", "occurredAtUtc", "organizationId", "customerTenantId", "requester", "approver", "executor",
        "eventKind", "serviceName", "resourceId", "settingName", "previousValue", "requestedValue", "verifiedValue",
        "outcome", "requestId", "planId", "approvalId", "operationId", "correlationId"
    ];

    public static string ToJson(IReadOnlyList<SettingAuditEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return JsonSerializer.Serialize(events.Select(PostgresAuditLedgerRepository.Redact), JsonOptions);
    }

    public static string ToCsv(IReadOnlyList<SettingAuditEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var output = new StringBuilder();
        output.AppendLine(string.Join(',', CsvHeaders.Select(EscapeCsv)));
        foreach (var item in events.Select(PostgresAuditLedgerRepository.Redact))
        {
            string?[] cells =
            [
                item.EventId.ToString("D"), item.OccurredAtUtc.ToString("O"),
                item.Scope.OrganizationId.ToString("D"), item.Scope.CustomerTenantId.ToString("D"),
                JsonSerializer.Serialize(item.Requester, JsonOptions),
                JsonSerializer.Serialize(item.Approver, JsonOptions),
                JsonSerializer.Serialize(item.Executor, JsonOptions),
                JsonSerializer.Serialize(item.EventKind, JsonOptions), item.ServiceName, item.ResourceId, item.SettingName,
                JsonSerializer.Serialize(item.PreviousValue, JsonOptions),
                JsonSerializer.Serialize(item.RequestedValue, JsonOptions),
                JsonSerializer.Serialize(item.VerifiedValue, JsonOptions),
                JsonSerializer.Serialize(item.Outcome, JsonOptions), item.RequestId.ToString("D"),
                item.PlanId?.ToString("D"), item.ApprovalId?.ToString("D"), item.OperationId.ToString("D"),
                item.CorrelationId.ToString("D")
            ];
            output.AppendLine(string.Join(',', cells.Select(EscapeCsv)));
        }

        return output.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        var safe = value ?? string.Empty;
        var firstMeaningfulCharacter = safe.FirstOrDefault(character => !char.IsWhiteSpace(character));
        if (safe.Length > 0 && (firstMeaningfulCharacter is '=' or '+' or '-' or '@' || safe[0] is '\t' or '\r' or '\n'))
        {
            safe = "'" + safe;
        }

        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}