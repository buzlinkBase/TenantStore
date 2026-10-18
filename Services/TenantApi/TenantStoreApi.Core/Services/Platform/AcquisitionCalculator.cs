namespace TenantStoreApi.Core.Services.Platform;

public enum AcquisitionBucket
{
    Day,
    Week,
    Month,
}

/// <summary>One account that signed up in the chosen range, with how far it has got since.</summary>
public sealed record AcquisitionAccount(
    Guid UserId,
    string Origin,
    DateTime? CreatedAtUtc,
    DateTime? EmailConfirmedAtUtc,
    DateTime? FirstHrisSignInAtUtc,
    DateTime? FirstPortalSignInAtUtc,
    DateTime? FirstCompanyAtUtc,
    DateTime? FirstSetupAtUtc,
    DateTime? TrialStartedAtUtc,
    DateTime? FirstPaidAtUtc);

/// <summary>An account owner whose first company / trial / payment fell in the range, whenever they signed up.</summary>
public sealed record AcquisitionOwner(
    Guid UserId,
    string Origin,
    DateTime? FirstCompanyAtUtc,
    DateTime? TrialStartedAtUtc,
    DateTime? FirstPaidAtUtc);

public sealed record AcquisitionSeries(string Origin, IReadOnlyList<int> Counts, int Total);

public sealed record AcquisitionFunnelStage(string Key, int Count);

public sealed record AcquisitionFunnel(string Origin, IReadOnlyList<AcquisitionFunnelStage> Stages);

public sealed record AcquisitionReport(
    DateOnly From,
    DateOnly To,
    string Bucket,
    /// <summary>First day of each bucket (Philippine time), oldest first. Every series lines up with this.</summary>
    IReadOnlyList<DateOnly> Buckets,
    IReadOnlyList<AcquisitionSeries> NewAccounts,
    IReadOnlyList<AcquisitionSeries> CompaniesCreated,
    IReadOnlyList<AcquisitionSeries> TrialsStarted,
    IReadOnlyList<AcquisitionSeries> FirstPayments,
    IReadOnlyList<AcquisitionFunnel> Funnels,
    /// <summary>Accounts with no recorded sign-up date (they can't be placed on the timeline).</summary>
    int UndatedAccounts,
    /// <summary>False when the origin data couldn't be fetched: origin series and funnels are then empty.</summary>
    bool OriginAvailable,
    bool Truncated);

/// <summary>One account's own trail, for the account page.</summary>
public sealed record AccountJourney(
    string Origin,
    DateTime? CreatedAtUtc,
    DateTime? EmailConfirmedAtUtc,
    DateTime? FirstHrisSignInAtUtc,
    DateTime? FirstPortalSignInAtUtc,
    DateTime? FirstCompanyAtUtc,
    DateTime? FirstSetupAtUtc,
    DateTime? TrialStartedAtUtc,
    DateTime? FirstPaidAtUtc,
    bool OriginAvailable);

/// <summary>
/// Turns accounts and owners into the Acquisition report's time series and funnels. Pure: no database, no clock.
/// Days, weeks (Monday start) and months are Philippine time (UTC+8), the time the owner reads the report in.
/// </summary>
public static class AcquisitionCalculator
{
    public const string Unknown = "Unknown";
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(8);

    /// <summary>The funnel's steps, in order. An account that reached a later step counts for every earlier one, so a
    /// person whose first sign-in was never recorded still counts as having signed in once they've paid.</summary>
    public static readonly IReadOnlyList<string> FunnelStages =
        ["created", "emailConfirmed", "signedIn", "company", "setUp", "trial", "paid"];

    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(LocalOffset));

    public static DateOnly BucketStart(DateOnly date, AcquisitionBucket bucket) => bucket switch
    {
        AcquisitionBucket.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        AcquisitionBucket.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };

    public static DateOnly NextBucket(DateOnly start, AcquisitionBucket bucket) => bucket switch
    {
        AcquisitionBucket.Week => start.AddDays(7),
        AcquisitionBucket.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };

    public static IReadOnlyList<DateOnly> Buckets(DateOnly from, DateOnly to, AcquisitionBucket bucket)
    {
        var list = new List<DateOnly>();
        for (var start = BucketStart(from, bucket); start <= to; start = NextBucket(start, bucket)) list.Add(start);
        return list;
    }

    public static AcquisitionReport Build(
        DateOnly from, DateOnly to, AcquisitionBucket bucket,
        IReadOnlyList<AcquisitionAccount> cohort, IReadOnlyList<AcquisitionOwner> owners,
        int undatedAccounts, bool originAvailable, bool truncated)
    {
        var buckets = Buckets(from, to, bucket);
        var index = buckets.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);

        AcquisitionSeries[] Series<T>(IEnumerable<T> items, Func<T, string> origin, Func<T, DateTime?> when)
        {
            var counts = new Dictionary<string, int[]>();
            foreach (var item in items)
            {
                if (when(item) is not { } at) continue;
                var date = LocalDate(at);
                if (date < from || date > to) continue;
                if (!index.TryGetValue(BucketStart(date, bucket), out var slot)) continue;
                var key = Normalize(origin(item));
                if (!counts.TryGetValue(key, out var row)) counts[key] = row = new int[buckets.Count];
                row[slot]++;
            }
            return counts.OrderBy(c => OriginOrder(c.Key))
                .Select(c => new AcquisitionSeries(c.Key, c.Value, c.Value.Sum())).ToArray();
        }

        var funnels = cohort
            .GroupBy(a => Normalize(a.Origin))
            .OrderBy(g => OriginOrder(g.Key))
            .Select(g => new AcquisitionFunnel(g.Key, FunnelStages
                .Select((stage, i) => new AcquisitionFunnelStage(stage, g.Count(a => Reached(a, i)))).ToArray()))
            .ToArray();

        return new AcquisitionReport(
            from, to, bucket.ToString().ToLowerInvariant(), buckets,
            Series(cohort, a => a.Origin, a => a.CreatedAtUtc),
            Series(owners, o => o.Origin, o => o.FirstCompanyAtUtc),
            Series(owners, o => o.Origin, o => o.TrialStartedAtUtc),
            Series(owners, o => o.Origin, o => o.FirstPaidAtUtc),
            funnels, undatedAccounts, originAvailable, truncated);
    }

    /// <summary>Whether the account reached funnel step <paramref name="stage"/> (or any later one).</summary>
    private static bool Reached(AcquisitionAccount a, int stage)
    {
        if (stage == 0) return true;
        bool[] hit =
        [
            true,
            a.EmailConfirmedAtUtc != null,
            a.FirstHrisSignInAtUtc != null,
            a.FirstCompanyAtUtc != null,
            a.FirstSetupAtUtc != null,
            a.TrialStartedAtUtc != null,
            a.FirstPaidAtUtc != null,
        ];
        for (var i = stage; i < hit.Length; i++)
            if (hit[i]) return true;
        return false;
    }

    private static string Normalize(string? origin) => string.IsNullOrWhiteSpace(origin) ? Unknown : origin;

    private static int OriginOrder(string origin) => origin switch
    {
        "EmailSignup" => 0,
        "GoogleSignup" => 1,
        "StaffInvite" => 2,
        "ClientInvite" => 3,
        _ => 4,
    };
}
