namespace Onepunch.AuditEngine;

/// <summary>
/// The only way anything records an event. Writing stages the event with the caller's current
/// unit of work -- it is persisted together with (and only if) the business change it describes,
/// when the host saves. A host recording an event with no accompanying change (e.g. a failed
/// login) must still save/commit afterwards.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken token = default);
}

/// <summary>
/// Persists what <see cref="IAuditWriter"/> has staged when the caller has no business change to save
/// it with -- a request log, a failed login. Implemented by each storage adapter.
/// </summary>
public interface IAuditCommitter
{
    Task CommitAsync(CancellationToken token = default);
}

/// <summary>Reads the trail back. Implemented by each storage adapter.</summary>
public interface IAuditQueryService
{
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken token = default);

    Task<AuditEvent?> GetAsync(Guid id, string? organizationId, CancellationToken token = default);
}

/// <summary>Filters for <see cref="IAuditQueryService.QueryAsync"/>; null means "any".</summary>
public sealed record AuditQuery
{
    public string? OrganizationId { get; init; }
    public string? ActorId { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? EventType { get; init; }
    public AuditOutcome? Outcome { get; init; }
    public DateTime? FromUtc { get; init; }
    /// <summary>Exclusive upper bound.</summary>
    public DateTime? ToUtc { get; init; }
    /// <summary>Free text matched against description, actor name and entity id.</summary>
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AuditPage(IReadOnlyList<AuditEvent> Items, int Total, int Page, int PageSize);

/// <summary>
/// Resolves who is acting and for which organization, from whatever identity scheme the host
/// already has. The engine core never touches claims or HTTP itself.
/// </summary>
public interface IAuditActorResolver
{
    AuditActor ResolveActor();

    string? ResolveOrganizationId();
}

/// <summary>Adds request context (IP, user agent, ...) to every event. Hosts may register several.</summary>
public interface IAuditMetadataEnricher
{
    void Enrich(IDictionary<string, string?> metadata);
}

/// <summary>Masks sensitive values before they are hashed or stored.</summary>
public interface IAuditRedactor
{
    /// <summary>Returns the value to record for <paramref name="property"/> of <paramref name="entityType"/>.</summary>
    string? Redact(string? entityType, string property, string? value);
}

/// <summary>The latest hash of an organization's chain, read from wherever events are stored.</summary>
public interface IAuditChainHead
{
    Task<string?> GetHeadHashAsync(string? organizationId, CancellationToken token = default);
}

/// <summary>
/// Stores an event recorded by another service exactly as received (already enriched and hashed)
/// -- for keeping a local read copy of several services' trails. Idempotent by event id.
/// </summary>
public interface IAuditReplicaWriter
{
    Task WriteReplicaAsync(AuditEvent auditEvent, CancellationToken token = default);
}

/// <summary>
/// Observes events staged in a save, inside that same save (before it commits). Used to forward
/// events elsewhere; with a transactional outbox the forward commits atomically with the events.
/// </summary>
public interface IAuditEventSink
{
    Task OnStagedAsync(IReadOnlyList<AuditEvent> events, CancellationToken token = default);
}
