namespace Onepunch.AuditEngine;

/// <summary>
/// One recorded action: who did what to which record, when, from where, and with what result.
/// Immutable -- each stage of the write pipeline (enrichment, redaction, hash chaining) hands the
/// next one a modified copy via <c>with</c>, so no stage can alter an event another stage has
/// already accounted for.
/// </summary>
public sealed record AuditEvent
{
    public Guid Id { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    /// <summary>Open-ended; see <see cref="AuditEventTypes"/> for the common values.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>The kind of record acted on, e.g. "Employee", "LeaveApplication", "User".</summary>
    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    /// <summary>
    /// The organization (company/tenant) the event belongs to. A plain string so the engine
    /// carries no host-specific tenancy model; null for events that belong to no organization
    /// (e.g. a failed login for an unknown email).
    /// </summary>
    public string? OrganizationId { get; init; }

    /// <summary>Null until the enrichment stage resolves the current actor.</summary>
    public AuditActor? Actor { get; init; }

    public string? Description { get; init; }

    /// <summary>Changed fields only (all set fields for a create).</summary>
    public IReadOnlyList<AuditChange> Changes { get; init; } = [];

    /// <summary>Request context and anything else a host attaches (IP, user agent, trace id, ...).</summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();

    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

    /// <summary>The service that recorded the event, e.g. "hrms-api", "auth-api".</summary>
    public string? Source { get; init; }

    /// <summary>SHA-256 over this event's content and <see cref="PreviousEventHash"/>.</summary>
    public string? Hash { get; init; }

    /// <summary>The <see cref="Hash"/> of the event before this one in the same organization's chain.</summary>
    public string? PreviousEventHash { get; init; }
}

/// <summary>One field's value before and after the action, as invariant-culture text.</summary>
public sealed record AuditChange(string Property, string? OldValue, string? NewValue);

public enum AuditOutcome
{
    Success,
    Failure,
}

/// <summary>Common <see cref="AuditEvent.EventType"/> values. Hosts may use their own.</summary>
public static class AuditEventTypes
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string Approve = "Approve";
    public const string Reject = "Reject";
    public const string View = "View";
    public const string Export = "Export";
}
