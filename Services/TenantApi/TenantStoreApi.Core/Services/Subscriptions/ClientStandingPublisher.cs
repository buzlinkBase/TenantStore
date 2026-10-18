using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>Tells the Client Portal that an agency's standing may have changed, so its clients' access is re-evaluated at once.</summary>
public interface IClientStandingPublisher
{
    Task PublishAsync(IReadOnlyCollection<Guid> tenantIds, string reason, CancellationToken token = default);
}

/// <summary>
/// An agency's standing (suspended, held, back in good standing) decides whether its clients may use the portal. When an
/// account change could have moved it, this sends a <see cref="ClientMembershipChanged"/> for every client-portal person of
/// the agency: the portal drops its cached resolution and signs out whoever is now blocked. A resend that changes nothing is
/// harmless; the reasons below are the only ones that can move it. Goes out through the outbox with the change itself.
/// </summary>
public sealed class ClientStandingPublisher(IUnitOfWorkService uow, IPublishEndpoint publisher) : IClientStandingPublisher
{
    public const string ChangeType = "StatusChanged";

    private static readonly HashSet<string> StandingReasons =
    [
        BillingEventTypes.Suspended,
        BillingEventTypes.SuspendedByAdmin,
        BillingEventTypes.Reinstated,
        BillingEventTypes.LegalHoldSet,
        BillingEventTypes.LegalHoldReleased,
        BillingEventTypes.PaymentRecorded,
        BillingEventTypes.PaymentVoided,
        BillingEventTypes.PlanSetByAdmin,
        BillingEventTypes.PeriodExtended,
        BillingEventTypes.TrialExtended,
        BillingEventTypes.Resumed,
        BillingEventTypes.CheckoutPaid,
    ];

    public static bool AffectsStanding(string reason) => StandingReasons.Contains(reason);

    public async Task PublishAsync(IReadOnlyCollection<Guid> tenantIds, string reason, CancellationToken token = default)
    {
        if (tenantIds.Count == 0 || !AffectsStanding(reason)) return;

        var people = await uow.Context.ClientMembershipRoles.AsNoTracking()
            .Where(l => tenantIds.Contains(l.UserMembership.TenantId) && l.UserMembership.UserId != Guid.Empty)
            .Select(l => new { l.UserMembership.UserId, l.UserMembership.TenantId, l.ClientEntityId })
            .Distinct()
            .ToListAsync(token);

        foreach (var person in people)
        {
            await publisher.Publish(new ClientMembershipChanged
            {
                UserId = person.UserId,
                TenantId = person.TenantId,
                ClientEntityId = person.ClientEntityId,
                ChangeType = ChangeType,
            }, token);
        }
    }
}
