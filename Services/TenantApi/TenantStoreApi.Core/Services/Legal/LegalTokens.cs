using System.Globalization;
using System.Text.RegularExpressions;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Core.Services.Legal;

/// <summary>
/// A {{name}} placeholder the Terms may use, filled in from the live platform settings when a version
/// is published -- so the Terms state the provider's details and the grace / retention periods the
/// system actually applies. Strategy pattern: a new token is one more entry in <see cref="LegalTokenCatalog"/>.
/// </summary>
public sealed record LegalToken(string Name, string Description, Func<SubscriptionOptions, string> Resolve);

public static class LegalTokenCatalog
{
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static readonly IReadOnlyList<LegalToken> All =
    [
        new("entity_name", "The provider's registered name (console setting).", o => o.LegalEntityName),
        new("address", "The provider's registered address (console setting).", o => o.LegalAddress),
        new("contact_email", "Legal and privacy contact email (console setting).", o => o.LegalContactEmail),
        new("venue_city", "City whose courts hear disputes (console setting).", o => o.LegalVenueCity),
        new("liability_cap_months", "Months of fees the liability cap covers (console setting).", o => Number(o.LiabilityCapMonths)),
        new("trial_days", "Free trial length in days.", o => Number(o.TrialDays)),
        new("grace_days", "Days of full access after an unpaid period ends.", o => Number(o.GraceDays)),
        new("purge_after_days", "Days a locked or canceled account's data is kept before it may be purged.", o => Number(o.PurgeAfterDays)),
        new("archive_retention_years", "Years the payroll and attendance compliance archive is kept.", o => Number(o.ArchiveRetentionYears)),
        new("locked_punch_retention_days", "Days biometric punches are still stored for a read-only company.", o => Number(o.LockedPunchRetentionDays)),
    ];

    public static LegalToken? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}

/// <summary>Fills the {{tokens}} of a legal document. Unknown tokens are refused, never left in the text.</summary>
public static partial class LegalTokenRenderer
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex TokenPattern();

    public static IReadOnlyList<string> UnknownTokens(string markdown) =>
        TokenPattern().Matches(markdown)
            .Select(m => m.Groups[1].Value)
            .Where(name => LegalTokenCatalog.Find(name) == null)
            .Distinct()
            .ToList();

    public static string Render(string markdown, SubscriptionOptions options)
    {
        var unknown = UnknownTokens(markdown);
        if (unknown.Count > 0)
            throw new SubscriptionRuleException($"Unknown placeholder(s): {string.Join(", ", unknown.Select(n => "{{" + n + "}}"))}.");
        return TokenPattern().Replace(markdown, m => LegalTokenCatalog.Find(m.Groups[1].Value)!.Resolve(options));
    }
}
