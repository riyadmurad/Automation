namespace M365.SecurityAssessment.Assessment;

public enum ConditionalAccessPolicyState
{
    Enabled,
    Disabled,
    ReportOnly,
    Unknown
}

public sealed record ConditionalAccessPolicyScope(
    string PolicyId,
    ConditionalAccessPolicyState State,
    IReadOnlySet<string> IncludedPrincipalIds,
    IReadOnlySet<string> ExcludedPrincipalIds,
    IReadOnlySet<string> IncludedApplicationIds,
    IReadOnlySet<string> ExcludedApplicationIds,
    bool AssignmentsComplete,
    bool ConditionsSupported,
    string? UnknownReason);

public enum ConditionalAccessScopeState
{
    Covered,
    Excluded,
    NotCovered,
    Overlap,
    ReportOnly,
    Unknown
}

public sealed record ConditionalAccessScopeResult(
    string PrincipalId,
    string ApplicationId,
    ConditionalAccessScopeState State,
    IReadOnlyList<string> MatchingPolicyIds,
    IReadOnlyList<string> ExcludingPolicyIds,
    IReadOnlyList<string> ReportOnlyPolicyIds,
    IReadOnlyList<string> Reasons);

public static class ConditionalAccessScopeEvaluator
{
    public static ConditionalAccessScopeResult Evaluate(
        string principalId,
        string applicationId,
        IReadOnlyList<ConditionalAccessPolicyScope> policies,
        bool policyInventoryComplete)
    {
        if (string.IsNullOrWhiteSpace(principalId) || string.IsNullOrWhiteSpace(applicationId))
        {
            throw new AssessmentContractException("Conditional Access evaluation requires concrete principal and application IDs.");
        }

        var matching = new List<string>();
        var excluding = new List<string>();
        var reportOnly = new List<string>();
        var unknown = new List<string>();
        foreach (var policy in policies)
        {
            if (policy.State == ConditionalAccessPolicyState.Disabled)
            {
                continue;
            }

            if (policy.State == ConditionalAccessPolicyState.Unknown || !policy.AssignmentsComplete || !policy.ConditionsSupported)
            {
                unknown.Add(policy.UnknownReason ?? $"Policy {policy.PolicyId} has unresolved scope or conditions.");
                continue;
            }

            var principalIncluded = policy.IncludedPrincipalIds.Contains("All") || policy.IncludedPrincipalIds.Contains(principalId);
            var applicationIncluded = policy.IncludedApplicationIds.Contains("All") || policy.IncludedApplicationIds.Contains(applicationId);
            if (!principalIncluded || !applicationIncluded)
            {
                continue;
            }

            if (policy.ExcludedPrincipalIds.Contains(principalId) || policy.ExcludedApplicationIds.Contains(applicationId))
            {
                excluding.Add(policy.PolicyId);
            }
            else if (policy.State == ConditionalAccessPolicyState.ReportOnly)
            {
                reportOnly.Add(policy.PolicyId);
            }
            else
            {
                matching.Add(policy.PolicyId);
            }
        }

        if (unknown.Count > 0 || !policyInventoryComplete)
        {
            if (!policyInventoryComplete)
            {
                unknown.Add("Conditional Access policy inventory is incomplete.");
            }
            return Result(ConditionalAccessScopeState.Unknown, matching, excluding, reportOnly, unknown);
        }

        var state = matching.Count > 1
            ? ConditionalAccessScopeState.Overlap
            : matching.Count == 1
                ? ConditionalAccessScopeState.Covered
                : excluding.Count > 0
                    ? ConditionalAccessScopeState.Excluded
                    : reportOnly.Count > 0
                        ? ConditionalAccessScopeState.ReportOnly
                        : ConditionalAccessScopeState.NotCovered;
        return Result(state, matching, excluding, reportOnly, []);

        ConditionalAccessScopeResult Result(
            ConditionalAccessScopeState state,
            IReadOnlyList<string> matches,
            IReadOnlyList<string> exclusions,
            IReadOnlyList<string> reportOnlyPolicies,
            IReadOnlyList<string> reasons) =>
            new(principalId, applicationId, state, matches, exclusions, reportOnlyPolicies, reasons);
    }
}