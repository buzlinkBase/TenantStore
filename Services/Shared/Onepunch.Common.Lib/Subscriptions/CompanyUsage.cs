namespace Onepunch.Common.Lib.Subscriptions;

/// <summary>
/// How much of each per-company limit a company uses, as reported by the service that owns the
/// data (hrms-api: employees; hrms-adms-api: biometric devices). Keyed by limit key
/// (<see cref="EntitlementKeys"/>) so a new limit needs no contract change. Answers
/// GET api/v1/internal/subscription-usage on those services.
/// </summary>
public sealed record CompanyUsage
{
    public Guid TenantId { get; init; }
    public Dictionary<string, int> Used { get; init; } = new();
}
