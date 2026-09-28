using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Onepunch.AuditEngine.MassTransit;

/// <summary>
/// An event some service recorded in its own trail, published for services keeping a read copy.
/// The recording service's own table stays the authoritative record.
/// </summary>
public sealed record AuditEventRecorded
{
    public Guid Id { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? OrganizationId { get; init; }
    public string? ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? ActorType { get; init; }
    public string? Description { get; init; }
    public List<AuditChange> Changes { get; init; } = [];
    public Dictionary<string, string?> Metadata { get; init; } = [];
    public string Outcome { get; init; } = nameof(AuditOutcome.Success);
    public string? Source { get; init; }
    public string? Hash { get; init; }
    public string? PreviousEventHash { get; init; }

    public static AuditEventRecorded From(AuditEvent e) => new()
    {
        Id = e.Id,
        OccurredAtUtc = e.OccurredAtUtc,
        EventType = e.EventType,
        EntityType = e.EntityType,
        EntityId = e.EntityId,
        OrganizationId = e.OrganizationId,
        ActorId = e.Actor?.Id,
        ActorDisplayName = e.Actor?.DisplayName,
        ActorType = e.Actor?.Type,
        Description = e.Description,
        Changes = e.Changes.ToList(),
        Metadata = e.Metadata.ToDictionary(x => x.Key, x => x.Value),
        Outcome = e.Outcome.ToString(),
        Source = e.Source,
        Hash = e.Hash,
        PreviousEventHash = e.PreviousEventHash,
    };

    public AuditEvent ToEvent() => new()
    {
        Id = Id,
        OccurredAtUtc = DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc),
        EventType = EventType,
        EntityType = EntityType,
        EntityId = EntityId,
        OrganizationId = OrganizationId,
        Actor = ActorId == null ? null : new AuditActor(ActorId, ActorDisplayName, ActorType ?? AuditActorTypes.User),
        Description = Description,
        Changes = Changes,
        Metadata = Metadata,
        Outcome = Enum.TryParse<AuditOutcome>(Outcome, out var outcome) ? outcome : AuditOutcome.Success,
        Source = Source,
        Hash = Hash,
        PreviousEventHash = PreviousEventHash,
    };
}

public sealed class MassTransitAuditForwardingOptions
{
    /// <summary>When set, the event's organization id is also sent in this message header.</summary>
    public string? OrganizationHeaderName { get; set; }

    /// <summary>
    /// Which events to forward. Default: those belonging to an organization -- an event with
    /// none has no organization's read copy to go to.
    /// </summary>
    public Func<AuditEvent, bool> ShouldForward { get; set; } = e => !string.IsNullOrWhiteSpace(e.OrganizationId);
}

/// <summary>
/// Publishes the events this service recorded, from inside the same save. With MassTransit's
/// transactional (EF) outbox the messages commit atomically with the audit rows; events this
/// service merely stored as replicas are never re-forwarded.
/// </summary>
public sealed class MassTransitAuditEventForwarder(
    IPublishEndpoint publisher,
    AuditEngineOptions engineOptions,
    MassTransitAuditForwardingOptions forwardingOptions) : IAuditEventSink
{
    public async Task OnStagedAsync(IReadOnlyList<AuditEvent> events, CancellationToken token = default)
    {
        foreach (var auditEvent in events)
        {
            if (auditEvent.Source != engineOptions.SourceName || !forwardingOptions.ShouldForward(auditEvent))
                continue;

            var message = AuditEventRecorded.From(auditEvent);
            var header = forwardingOptions.OrganizationHeaderName;
            if (header == null || auditEvent.OrganizationId == null)
                await publisher.Publish(message, token);
            else
                await publisher.Publish(message, (PublishContext<AuditEventRecorded> ctx) => ctx.Headers.Set(header, auditEvent.OrganizationId), token);
        }
    }
}

public static class MassTransitAuditBuilderExtensions
{
    public static AuditEngineBuilder ForwardToMassTransit(this AuditEngineBuilder builder, Action<MassTransitAuditForwardingOptions>? configure = null)
    {
        var options = new MassTransitAuditForwardingOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        return builder.AddSink<MassTransitAuditEventForwarder>();
    }
}
