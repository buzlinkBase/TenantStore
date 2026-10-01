using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>An email that's due, and the key that makes sure it's sent once.</summary>
public sealed record ReminderDecision(string Template, string DedupeKey);

/// <summary>
/// Strategy: one rule per subscription email. The lifecycle job asks every rule about every
/// subscription on each run; a rule answers with the email due right now, if any. The dedupe key
/// (recorded as a BillingEvent) is what stops the hourly run from sending it twice -- so it names
/// the occurrence, e.g. the period end a payment reminder is for.
/// </summary>
public interface ISubscriptionReminderRule
{
    ReminderDecision? Evaluate(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options);
}

/// <summary>A rule defined by a predicate producing the occurrence part of the dedupe key.</summary>
public sealed class SubscriptionReminderRule(
    string template,
    Func<AccountSubscription, DateTime, SubscriptionOptions, string?> occurrence) : ISubscriptionReminderRule
{
    public ReminderDecision? Evaluate(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options) =>
        occurrence(subscription, nowUtc, options) is { } key
            ? new ReminderDecision(template, $"email:{template}:{subscription.Id:N}:{key}")
            : null;
}

/// <summary>The trial and billing email schedule.</summary>
public static class SubscriptionReminderSchedule
{
    private static readonly int[] ExpiredReminderDays = [1, 3, 7];
    private static readonly int[] PurgeWarningDays = [30, 7, 1];

    public static IReadOnlyList<ISubscriptionReminderRule> Rules { get; } = Build();

    private static List<ISubscriptionReminderRule> Build()
    {
        var rules = new List<ISubscriptionReminderRule>
        {
            // Day 1.
            new SubscriptionReminderRule(SubscriptionEmailTemplates.TrialWelcome, (s, _, _) =>
                s.SubStatus == AccountSubscriptionStatus.Trialing ? "once" : null),

            // Day 4: what they've set up so far.
            new SubscriptionReminderRule(SubscriptionEmailTemplates.TrialUsageSummary, (s, now, o) =>
                s.SubStatus == AccountSubscriptionStatus.Trialing && TrialDay(s, now, o) >= 4 ? "once" : null),

            // Day 6: the last full day.
            new SubscriptionReminderRule(SubscriptionEmailTemplates.TrialEndsTomorrow, (s, now, _) =>
                s.SubStatus == AccountSubscriptionStatus.Trialing
                && s.TrialEndsAtUtc is { } ends && ends > now && ends - now <= TimeSpan.FromDays(1)
                    ? Day(ends) : null),

            // Day 7: ended today.
            new SubscriptionReminderRule(SubscriptionEmailTemplates.TrialEnded, (s, now, _) =>
                IsUnpaidExpiredTrial(s) && s.TrialEndsAtUtc is { } ends && now - ends < TimeSpan.FromDays(1)
                    ? Day(ends) : null),

            // Seven days before a paid period ends (not when it's been canceled).
            new SubscriptionReminderRule(SubscriptionEmailTemplates.PaymentDue, (s, now, _) =>
                s.SubStatus == AccountSubscriptionStatus.Active && !s.CancelAtPeriodEnd
                && s.CurrentPeriodEndUtc is { } end && end > now && end - now <= TimeSpan.FromDays(7)
                    ? Day(end) : null),

            new SubscriptionReminderRule(SubscriptionEmailTemplates.PastDue, (s, _, _) =>
                s.SubStatus == AccountSubscriptionStatus.PastDue && s.GraceEndsAtUtc is { } grace ? Day(grace) : null),

            new SubscriptionReminderRule(SubscriptionEmailTemplates.Suspended, (s, _, _) =>
                s.SubStatus == AccountSubscriptionStatus.Suspended && s.GraceEndsAtUtc is { } grace ? Day(grace) : null),

            new SubscriptionReminderRule(SubscriptionEmailTemplates.Canceled, (s, _, _) =>
                s.SubStatus == AccountSubscriptionStatus.Canceled && s.CanceledAtUtc is { } at ? Day(at) : null),
        };

        // Days +1, +3 and +7 after an unpaid trial ended. Each fires only within its own day, so a
        // job that was down for a week doesn't send all three at once.
        rules.AddRange(ExpiredReminderDays.Select(day => new SubscriptionReminderRule(
            SubscriptionEmailTemplates.TrialExpiredReminder, (s, now, _) =>
                IsUnpaidExpiredTrial(s) && s.TrialEndsAtUtc is { } ends
                && now - ends >= TimeSpan.FromDays(day) && now - ends < TimeSpan.FromDays(day + 1)
                    ? $"d{day}:{Day(ends)}" : null)));

        // Before a canceled account's data may be purged.
        rules.AddRange(PurgeWarningDays.Select(days => new SubscriptionReminderRule(
            SubscriptionEmailTemplates.PurgeWarning, (s, now, _) =>
                s.SubStatus == AccountSubscriptionStatus.Canceled && s.PurgeAfterUtc is { } purge
                && purge > now && purge - now <= TimeSpan.FromDays(days) && purge - now > TimeSpan.FromDays(days - 1)
                    ? $"d{days}:{Day(purge)}" : null)));

        return rules;
    }

    /// <summary>1 on the day the trial started.</summary>
    public static int TrialDay(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options)
    {
        var start = subscription.CreatedAt != default
            ? subscription.CreatedAt
            : (subscription.TrialEndsAtUtc ?? nowUtc).AddDays(-options.TrialDays);
        return (int)Math.Floor((nowUtc - start).TotalDays) + 1;
    }

    private static bool IsUnpaidExpiredTrial(AccountSubscription subscription) =>
        subscription.SubStatus == AccountSubscriptionStatus.Expired && subscription.CurrentPeriodEndUtc == null;

    private static string Day(DateTime value) => value.ToString("yyyyMMdd");
}
