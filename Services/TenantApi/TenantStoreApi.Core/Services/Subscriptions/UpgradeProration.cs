using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>Money already paid for a span of time: a plan period, or storage blocks bought mid-period.</summary>
public sealed record PaidSpan(decimal Value, DateTime StartUtc, DateTime EndUtc);

/// <summary>What the unused part of the running period is worth.</summary>
/// <param name="UnusedDays">Whole days left in the current paid period (today counts as used).</param>
public sealed record UpgradeCredit(decimal Amount, int UnusedDays)
{
    public static readonly UpgradeCredit None = new(0m, 0);
}

/// <summary>What switching plans mid-period costs once the unused time is credited.</summary>
/// <param name="Charge">The new plan's full-period price plus the storage renewal, before the credit.</param>
/// <param name="CreditApplied">All of the credit -- any part the charge doesn't use becomes <paramref name="ExtraDays"/>.</param>
/// <param name="AmountDue">What's left to pay: never negative.</param>
/// <param name="PeriodEndUtc">The new period's end, extra days included.</param>
public sealed record UpgradeCharge(
    decimal Charge,
    decimal CreditApplied,
    decimal AmountDue,
    DateTime PeriodEndUtc,
    int ExtraDays,
    int UnusedDays);

/// <summary>
/// The money rules for an upgrade before the paid period ends, free of any I/O so they are easy to
/// test. The new plan starts today for a full period; what was paid for the days that won't be used
/// is taken off its price, so no day is paid for twice. Credit beyond the price buys extra days.
/// Whole days only: an amount stays the same all day, so a quote and its checkout agree.
/// </summary>
public static class UpgradeProration
{
    /// <summary>The unused value of what was paid: each span's value for the whole days still to run.</summary>
    public static UpgradeCredit CreditFor(IEnumerable<PaidSpan> spans, DateTime nowUtc)
    {
        var credit = 0m;
        var unusedDays = 0;
        foreach (var span in spans)
        {
            var totalDays = (int)Math.Round((span.EndUtc - span.StartUtc).TotalDays, MidpointRounding.AwayFromZero);
            if (totalDays <= 0 || span.Value <= 0) continue;

            var from = span.StartUtc > nowUtc ? span.StartUtc : nowUtc;
            var unused = Math.Clamp((int)Math.Floor((span.EndUtc - from).TotalDays), 0, totalDays);
            if (unused == 0) continue;

            credit += span.Value * unused / totalDays;
            unusedDays = Math.Max(unusedDays, (int)Math.Floor((span.EndUtc - nowUtc).TotalDays));
        }
        return new UpgradeCredit(Math.Round(credit, 2, MidpointRounding.AwayFromZero), unusedDays);
    }

    /// <summary>Applies a credit to the charge for a new period starting at <paramref name="startUtc"/>.</summary>
    public static UpgradeCharge Apply(decimal charge, UpgradeCredit credit, DateTime startUtc, BillingCycle cycle)
    {
        var periodEnd = AccountSubscriptionService.AddCycle(startUtc, cycle);
        var amountDue = Math.Max(0m, charge - credit.Amount);
        var extraDays = 0;

        var excess = credit.Amount - charge;
        var cycleDays = (decimal)(periodEnd - startUtc).TotalDays;
        if (excess > 0 && charge > 0 && cycleDays > 0)
        {
            var dailyRate = charge / cycleDays;
            extraDays = (int)Math.Floor(excess / dailyRate);
        }

        return new UpgradeCharge(charge, credit.Amount, amountDue, periodEnd.AddDays(extraDays), extraDays, credit.UnusedDays);
    }
}
