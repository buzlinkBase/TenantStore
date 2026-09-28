using Onepunch.AuditEngine.Integrity;

namespace Onepunch.AuditEngine.Pipeline;

/// <summary>
/// Decorator that completes an event before it is hashed and stored: id, timestamp, actor,
/// organization, source service, request metadata, and redaction of sensitive values. Anything
/// the caller already set is kept -- e.g. a login event names the user who is signing in, since
/// the request itself is still anonymous.
/// </summary>
public sealed class EnrichingAuditWriter(
    IAuditWriter inner,
    IAuditActorResolver actorResolver,
    IEnumerable<IAuditMetadataEnricher> enrichers,
    IAuditRedactor redactor,
    AuditEngineOptions options) : IAuditWriter
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken token = default)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var enricher in enrichers)
            enricher.Enrich(metadata);
        foreach (var (key, value) in auditEvent.Metadata)
            metadata[key] = value;

        var enriched = auditEvent with
        {
            Id = auditEvent.Id == Guid.Empty ? Guid.NewGuid() : auditEvent.Id,
            OccurredAtUtc = AuditHashCalculator.NormalizeTimestamp(
                auditEvent.OccurredAtUtc == default ? DateTime.UtcNow : auditEvent.OccurredAtUtc),
            Actor = auditEvent.Actor ?? actorResolver.ResolveActor(),
            OrganizationId = auditEvent.OrganizationId ?? actorResolver.ResolveOrganizationId(),
            Source = auditEvent.Source ?? options.SourceName,
            Changes = auditEvent.Changes
                .Select(c => c with
                {
                    OldValue = redactor.Redact(auditEvent.EntityType, c.Property, c.OldValue),
                    NewValue = redactor.Redact(auditEvent.EntityType, c.Property, c.NewValue),
                })
                .ToList(),
            Metadata = metadata,
        };

        return inner.WriteAsync(enriched, token);
    }
}

/// <summary>Stands in for the pipeline when auditing is disabled by configuration.</summary>
public sealed class NullAuditWriter : IAuditWriter
{
    public static readonly NullAuditWriter Instance = new();

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken token = default) => Task.CompletedTask;
}
