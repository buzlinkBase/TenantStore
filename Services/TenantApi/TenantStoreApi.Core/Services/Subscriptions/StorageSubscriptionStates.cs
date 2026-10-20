using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>A storage status change the lifecycle made, recorded as a billing event of <see cref="EventType"/>.</summary>
public sealed record StorageTransition(
    StorageSubscriptionStatus From,
    StorageSubscriptionStatus To,
    string EventType);

/// <summary>
/// State pattern, like <see cref="ISubscriptionState"/> for the plan: one class per
/// <see cref="StorageSubscriptionStatus"/>, each deciding (by date alone) when it moves on. Paying and
/// buying are commands on <see cref="StorageSubscriptionService"/>; only time passing is handled here.
/// </summary>
public interface IStorageSubscriptionState
{
    StorageSubscriptionStatus Status { get; }

    /// <param name="hasPaidBlocks">Whether any paid block is left to bill once due reductions applied.</param>
    StorageTransition? Advance(StorageSubscription storage, DateTime nowUtc, bool hasPaidBlocks, SubscriptionOptions options);
}

/// <summary>Paid and current. At the period end it lapses when nothing paid is left to renew (or every
/// paid block was given up), otherwise it falls past due with a grace period.</summary>
public sealed class StorageActiveState : IStorageSubscriptionState
{
    public StorageSubscriptionStatus Status => StorageSubscriptionStatus.Active;

    public StorageTransition? Advance(StorageSubscription storage, DateTime nowUtc, bool hasPaidBlocks, SubscriptionOptions options)
    {
        if (storage.CurrentPeriodEndUtc is not { } periodEnd || nowUtc < periodEnd) return null;

        if (storage.CancelAtPeriodEnd || !hasPaidBlocks)
        {
            storage.Status = StorageSubscriptionStatus.Lapsed;
            storage.CancelAtPeriodEnd = false;
            return new(Status, StorageSubscriptionStatus.Lapsed, BillingEventTypes.StorageLapsed);
        }

        storage.Status = StorageSubscriptionStatus.PastDue;
        storage.GraceEndsAtUtc = periodEnd.AddDays(options.StorageGraceDays);
        return new(Status, StorageSubscriptionStatus.PastDue, BillingEventTypes.StoragePastDue);
    }
}

/// <summary>The period ended unpaid: the blocks still count until the grace ends.</summary>
public sealed class StoragePastDueState : IStorageSubscriptionState
{
    public StorageSubscriptionStatus Status => StorageSubscriptionStatus.PastDue;

    public StorageTransition? Advance(StorageSubscription storage, DateTime nowUtc, bool hasPaidBlocks, SubscriptionOptions options)
    {
        storage.GraceEndsAtUtc ??= (storage.CurrentPeriodEndUtc ?? nowUtc).AddDays(options.StorageGraceDays);
        if (hasPaidBlocks && nowUtc < storage.GraceEndsAtUtc) return null;

        storage.Status = StorageSubscriptionStatus.Lapsed;
        return new(Status, StorageSubscriptionStatus.Lapsed, BillingEventTypes.StorageLapsed);
    }
}

/// <summary>Unpaid past the grace: paid blocks don't count. Only paying moves it on.</summary>
public sealed class StorageLapsedState : IStorageSubscriptionState
{
    public StorageSubscriptionStatus Status => StorageSubscriptionStatus.Lapsed;

    public StorageTransition? Advance(StorageSubscription storage, DateTime nowUtc, bool hasPaidBlocks, SubscriptionOptions options) => null;
}

/// <summary>Picks the state object for a storage subscription's status and runs its time-based transitions.</summary>
public sealed class StorageSubscriptionStateMachine
{
    private const int MaxStepsPerRun = 3;

    private readonly Dictionary<StorageSubscriptionStatus, IStorageSubscriptionState> _states;

    public StorageSubscriptionStateMachine() : this(
        [new StorageActiveState(), new StoragePastDueState(), new StorageLapsedState()])
    {
    }

    public StorageSubscriptionStateMachine(IEnumerable<IStorageSubscriptionState> states)
    {
        _states = states.ToDictionary(s => s.Status);
    }

    public List<StorageTransition> Advance(StorageSubscription storage, DateTime nowUtc, bool hasPaidBlocks, SubscriptionOptions options)
    {
        var transitions = new List<StorageTransition>();
        for (var step = 0; step < MaxStepsPerRun; step++)
        {
            var transition = _states[storage.Status].Advance(storage, nowUtc, hasPaidBlocks, options);
            if (transition == null) break;
            transitions.Add(transition);
        }
        return transitions;
    }

    /// <summary>
    /// The status the dates say it has at <paramref name="nowUtc"/>, even before the hourly job has saved
    /// it -- so capacity never outlives what was paid just because a run was late. A row past its period
    /// end but not yet moved on counts as past due (its grace end is decided when the job runs).
    /// </summary>
    public static StorageSubscriptionStatus StatusAt(StorageSubscription storage, DateTime nowUtc) => storage.Status switch
    {
        StorageSubscriptionStatus.Lapsed => StorageSubscriptionStatus.Lapsed,
        StorageSubscriptionStatus.PastDue => storage.GraceEndsAtUtc is { } grace && nowUtc >= grace
            ? StorageSubscriptionStatus.Lapsed
            : StorageSubscriptionStatus.PastDue,
        _ => storage.CurrentPeriodEndUtc is { } end && nowUtc >= end
            ? storage.CancelAtPeriodEnd ? StorageSubscriptionStatus.Lapsed : StorageSubscriptionStatus.PastDue
            : StorageSubscriptionStatus.Active,
    };

    /// <summary>Whether paid blocks add capacity at <paramref name="nowUtc"/>. With no storage period
    /// on record (rows from before storage had its own billing) they do, as they always did.</summary>
    public static bool PaidBlocksCount(StorageSubscription? storage, DateTime nowUtc) =>
        storage == null || StatusAt(storage, nowUtc) != StorageSubscriptionStatus.Lapsed;
}
