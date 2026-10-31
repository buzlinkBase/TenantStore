using System.Net;
using Onepunch.Common.Lib.DTO;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// The short note an owner gets when an operator erased their account or one of their companies and ticked
/// "email the owner a confirmation". It names what was erased and when, and holds none of the erased data.
/// Returns null when there is nobody to write to (an empty address), so the caller just skips the send.
/// </summary>
public static class ErasureEmailComposer
{
    public const string Source = "tenant";
    private const string Support = "support@onepunch.site";

    public static SendEmailRequested? ForAccount(string? ownerEmail, IReadOnlyList<string> companyNames, DateTime deletedAtUtc)
    {
        var what = companyNames.Count == 0
            ? "your One Punch HRIS account"
            : $"your One Punch HRIS account and {(companyNames.Count == 1 ? "its company" : "its companies")}: {string.Join(", ", companyNames)}";
        return Compose(ownerEmail, "Your One Punch HRIS account has been deleted", what, deletedAtUtc);
    }

    public static SendEmailRequested? ForCompany(string? ownerEmail, string companyName, DateTime deletedAtUtc) =>
        Compose(ownerEmail, $"{companyName} has been deleted from One Punch HRIS", $"the company {companyName}", deletedAtUtc);

    private static SendEmailRequested? Compose(string? ownerEmail, string subject, string what, DateTime deletedAtUtc)
    {
        var to = ownerEmail?.Trim();
        if (string.IsNullOrEmpty(to)) return null;

        // The Philippines has no daylight saving time, so UTC+8 is always right.
        var when = $"{deletedAtUtc.AddHours(8):MMMM d, yyyy h:mm tt} (Philippine time)";
        var text =
            $"We've permanently deleted {what} from One Punch HRIS on {when}, as requested.\n\n" +
            "This can't be undone. Records the law requires us to keep may be held in a read-only archive, as described in our Terms of Service.\n\n" +
            $"If you didn't expect this, contact us at {Support}.";
        var html =
            $"<p>We've permanently deleted <strong>{WebUtility.HtmlEncode(what)}</strong> from One Punch HRIS on {WebUtility.HtmlEncode(when)}, as requested.</p>" +
            "<p>This can't be undone. Records the law requires us to keep may be held in a read-only archive, as described in our Terms of Service.</p>" +
            $"<p>If you didn't expect this, contact us at <a href=\"mailto:{Support}\">{Support}</a>.</p>";

        return new SendEmailRequested
        {
            To = to,
            Subject = subject,
            HtmlBody = html,
            TextBody = text,
            Source = Source,
        };
    }
}
