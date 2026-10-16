using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Core;
using Serilog;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>One account's origin and early steps, as AuthApi recorded them.</summary>
public sealed record AccountOriginRecord(
    Guid UserId,
    string Origin,
    DateTime? CreatedAtUtc,
    DateTime? EmailConfirmedAtUtc,
    DateTime? FirstHrisSignInAtUtc,
    DateTime? FirstPortalSignInAtUtc,
    DateTime? OwnCompanyRequestedAtUtc);

public sealed record AccountOriginResult(IReadOnlyList<AccountOriginRecord> Accounts, int UndatedCount, bool Truncated, bool Available)
{
    public static AccountOriginResult Unavailable { get; } = new([], 0, false, false);
}

/// <summary>Where accounts came from. Strategy, so tests can stand in for AuthApi.</summary>
public interface IAccountOriginSource
{
    /// <summary>Everyone who signed up in [fromUtc, toUtc).</summary>
    Task<AccountOriginResult> GetCreatedBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken token);

    Task<AccountOriginResult> GetByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token);
}

/// <summary>AuthApi's record of how each account began, over gRPC. Never throws: a failure is reported as "unavailable".</summary>
public sealed class AuthAccountOriginSource(AccountOriginsService.AccountOriginsServiceClient client) : IAccountOriginSource
{
    private const int IdsPerCall = 1000;

    public async Task<AccountOriginResult> GetCreatedBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken token)
    {
        var request = new AccountOriginsRequest
        {
            CreatedFromUtc = fromUtc.ToString("O", CultureInfo.InvariantCulture),
            CreatedToUtc = toUtc.ToString("O", CultureInfo.InvariantCulture),
        };
        return await CallAsync(request, token);
    }

    public async Task<AccountOriginResult> GetByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token)
    {
        if (userIds.Count == 0) return new AccountOriginResult([], 0, false, true);
        var accounts = new List<AccountOriginRecord>();
        var truncated = false;
        foreach (var chunk in userIds.Distinct().Chunk(IdsPerCall))
        {
            var request = new AccountOriginsRequest();
            request.UserIds.AddRange(chunk.Select(id => id.ToString()));
            var part = await CallAsync(request, token);
            if (!part.Available) return AccountOriginResult.Unavailable;
            accounts.AddRange(part.Accounts);
            truncated |= part.Truncated;
        }
        return new AccountOriginResult(accounts, 0, truncated, true);
    }

    private async Task<AccountOriginResult> CallAsync(AccountOriginsRequest request, CancellationToken token)
    {
        try
        {
            var response = await client.GetAccountOriginsAsync(request, deadline: DateTime.UtcNow.AddSeconds(20), cancellationToken: token);
            var accounts = response.Accounts
                .Where(a => Guid.TryParse(a.UserId, out _))
                .Select(a => new AccountOriginRecord(
                    Guid.Parse(a.UserId), a.Origin,
                    Parse(a.CreatedAtUtc), Parse(a.EmailConfirmedAtUtc), Parse(a.FirstHrisSignInAtUtc),
                    Parse(a.FirstPortalSignInAtUtc), Parse(a.OwnCompanyRequestedAtUtc)))
                .ToList();
            return new AccountOriginResult(accounts, response.UndatedCount, response.Truncated, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Warning(ex, "Couldn't read account origins from AuthApi; the Acquisition report will leave origin out");
            return AccountOriginResult.Unavailable;
        }
    }

    private static DateTime? Parse(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;
}

/// <summary>
/// Where accounts come from and how they progress, for the Platform Console's Acquisition page: combines what AuthApi
/// knows (how an account began, sign-ins) with what this service knows (companies, trials, payments). History queries
/// ignore soft-delete, so an account that later left still counts. Read-only.
/// </summary>
public class PlatformAcquisitionService(IUnitOfWorkService uow, IAccountOriginSource origins)
{
    /// <summary>Longest range one report covers (about two years of days would be a lot of buckets; the console offers far less).</summary>
    public const int MaxRangeDays = 800;

    private TenantContext Context => uow.Context;

    public async Task<AcquisitionReport> GetReportAsync(DateOnly from, DateOnly to, AcquisitionBucket bucket, CancellationToken token = default)
    {
        if (to < from) throw new SubscriptionRuleException("The end of the range is before its start.");
        if (to.DayNumber - from.DayNumber > MaxRangeDays) throw new SubscriptionRuleException($"Choose a range of at most {MaxRangeDays} days.");
        if (bucket == AcquisitionBucket.Day && to.DayNumber - from.DayNumber > 120)
            throw new SubscriptionRuleException("Daily detail covers at most 120 days; choose weeks or months for a longer range.");

        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to.AddDays(1));

        // Who signed up in the range, with how they began.
        var created = await origins.GetCreatedBetweenAsync(fromUtc, toUtc, token);
        var cohortIds = created.Accounts.Select(a => a.UserId).ToList();
        var cohortDownstream = await DownstreamAsync(cohortIds, token);
        var cohort = created.Accounts.Select(a =>
        {
            cohortDownstream.TryGetValue(a.UserId, out var d);
            return new AcquisitionAccount(
                a.UserId, a.Origin, a.CreatedAtUtc, a.EmailConfirmedAtUtc, a.FirstHrisSignInAtUtc, a.FirstPortalSignInAtUtc,
                Earliest(d?.FirstCompanyAtUtc, a.OwnCompanyRequestedAtUtc), d?.FirstSetupAtUtc, d?.TrialStartedAtUtc, d?.FirstPaidAtUtc);
        }).ToList();

        // Owners whose first company, trial or payment fell in the range (they may have signed up long before it).
        var milestoneOwners = await MilestoneOwnersAsync(fromUtc, toUtc, token);
        var ownerOrigins = await origins.GetByIdsAsync(milestoneOwners.Keys.ToList(), token);
        var originOf = ownerOrigins.Available ? ownerOrigins.Accounts.ToDictionary(a => a.UserId, a => a.Origin) : [];
        var owners = milestoneOwners.Select(o => new AcquisitionOwner(
            o.Key, originOf.GetValueOrDefault(o.Key, AcquisitionCalculator.Unknown),
            o.Value.FirstCompanyAtUtc, o.Value.TrialStartedAtUtc, o.Value.FirstPaidAtUtc)).ToList();

        return AcquisitionCalculator.Build(from, to, bucket, cohort, owners, created.UndatedCount,
            created.Available && ownerOrigins.Available, created.Truncated);
    }

    /// <summary>The trail of one account: how it began and each step since.</summary>
    public async Task<AccountJourney> GetJourneyAsync(Guid userId, CancellationToken token = default)
    {
        var origin = (await origins.GetByIdsAsync([userId], token));
        var record = origin.Accounts.FirstOrDefault();
        var downstream = (await DownstreamAsync([userId], token)).GetValueOrDefault(userId);
        return new AccountJourney(
            record?.Origin ?? AcquisitionCalculator.Unknown,
            record?.CreatedAtUtc, record?.EmailConfirmedAtUtc, record?.FirstHrisSignInAtUtc, record?.FirstPortalSignInAtUtc,
            Earliest(downstream?.FirstCompanyAtUtc, record?.OwnCompanyRequestedAtUtc),
            downstream?.FirstSetupAtUtc, downstream?.TrialStartedAtUtc, downstream?.FirstPaidAtUtc,
            origin.Available);
    }

    private sealed record Downstream(DateTime? FirstCompanyAtUtc, DateTime? FirstSetupAtUtc, DateTime? TrialStartedAtUtc, DateTime? FirstPaidAtUtc);

    /// <summary>First company, first setup, trial start and first live payment for each of the given owners.</summary>
    private async Task<Dictionary<Guid, Downstream>> DownstreamAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token)
    {
        var result = new Dictionary<Guid, Downstream>();
        foreach (var chunk in userIds.Distinct().Chunk(1000))
        {
            var ids = chunk.ToList();
            var companies = await Context.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Where(t => ids.Contains(t.UserId))
                .GroupBy(t => t.UserId)
                .Select(g => new { UserId = g.Key, Created = (DateTime?)g.Min(t => t.CreatedAt), Setup = g.Min(t => t.ActivatedAtUtc) })
                .ToDictionaryAsync(x => x.UserId, token);
            var trials = await TrialsQuery().Where(e => ids.Contains(e.OwnerUserId!.Value))
                .GroupBy(e => e.OwnerUserId!.Value)
                .Select(g => new { UserId = g.Key, At = (DateTime?)g.Min(e => e.OccurredAtUtc) })
                .ToDictionaryAsync(x => x.UserId, x => x.At, token);
            var paid = await PaymentsQuery().Where(p => ids.Contains(p.Subscription.OwnerUserId))
                .GroupBy(p => p.Subscription.OwnerUserId)
                .Select(g => new { UserId = g.Key, At = (DateTime?)g.Min(p => p.PaidAtUtc) })
                .ToDictionaryAsync(x => x.UserId, x => x.At, token);

            foreach (var id in ids)
            {
                companies.TryGetValue(id, out var company);
                result[id] = new Downstream(company?.Created, company?.Setup, trials.GetValueOrDefault(id), paid.GetValueOrDefault(id));
            }
        }
        return result;
    }

    private async Task<Dictionary<Guid, Downstream>> MilestoneOwnersAsync(DateTime fromUtc, DateTime toUtc, CancellationToken token)
    {
        var companies = await Context.Tenants.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, At = g.Min(t => t.CreatedAt) })
            .Where(x => x.At >= fromUtc && x.At < toUtc)
            .ToListAsync(token);
        var trials = await TrialsQuery()
            .GroupBy(e => e.OwnerUserId!.Value)
            .Select(g => new { UserId = g.Key, At = g.Min(e => e.OccurredAtUtc) })
            .Where(x => x.At >= fromUtc && x.At < toUtc)
            .ToListAsync(token);
        var paid = await PaymentsQuery()
            .GroupBy(p => p.Subscription.OwnerUserId)
            .Select(g => new { UserId = g.Key, At = g.Min(p => p.PaidAtUtc) })
            .Where(x => x.At >= fromUtc && x.At < toUtc)
            .ToListAsync(token);

        var owners = new Dictionary<Guid, Downstream>();
        void Merge(Guid id, Func<Downstream, Downstream> change) =>
            owners[id] = change(owners.GetValueOrDefault(id) ?? new Downstream(null, null, null, null));
        foreach (var c in companies) Merge(c.UserId, d => d with { FirstCompanyAtUtc = c.At });
        foreach (var t in trials) Merge(t.UserId, d => d with { TrialStartedAtUtc = t.At });
        foreach (var p in paid) Merge(p.UserId, d => d with { FirstPaidAtUtc = p.At });
        return owners;
    }

    // A real trial start only: the one-off legacy backfill wrote its own event type for accounts that pre-date trials.
    private IQueryable<Domain.Entities.Subs.BillingEvent> TrialsQuery() =>
        Context.BillingEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Type == BillingEventTypes.TrialStarted && e.OwnerUserId != null);

    // Money actually received: voided payments (recorded by mistake, refunded, fell through) don't count.
    private IQueryable<Domain.Entities.Subs.SubscriptionPayment> PaymentsQuery() =>
        Context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking().Where(p => p.VoidedAtUtc == null);

    private static DateTime ToUtc(DateOnly localDate) =>
        DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue) - AcquisitionCalculator.LocalOffset, DateTimeKind.Utc);

    private static DateTime? Earliest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a < b ? a : b;
}
