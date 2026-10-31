using System.Net;
using Onepunch.Common.Lib.DTO;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// The emails of an owner closing their account: the confirmation (with a "this wasn't me" link), what
/// happens next once they confirm, and the operator's decision. Plain notes; none holds company data.
/// Return null when there is no address to write to.
/// </summary>
public static class AccountClosureEmailComposer
{
    public const string Source = "tenant";
    private const string Support = "support@onepunch.site";

    public static SendEmailRequested? Confirm(string? to, string confirmUrl, string cancelUrl, DateTime confirmExpiresUtc) =>
        Compose(to, "Confirm closing your One Punch HRIS account",
            "Someone asked to close your One Punch HRIS account. If that was you, confirm it with the link below. " +
            $"It works until {Ph(confirmExpiresUtc)}. Nothing is closed until you confirm, and even then you get a few days to change your mind.",
            ("Confirm closing my account", confirmUrl),
            ("This wasn't me, cancel the request", cancelUrl));

    public static SendEmailRequested? Scheduled(string? to, DateTime coolingOffEndsUtc) =>
        Compose(to, "Your account closure request is confirmed",
            $"We've received your confirmation. Until {Ph(coolingOffEndsUtc)} you can still change your mind: sign in and cancel under Billing, or use the \"this wasn't me\" link in the first email. " +
            "After that, our team reviews the request. Nothing has been closed or deleted yet.");

    public static SendEmailRequested? Approved(string? to, DateTime purgeAfterUtc) =>
        Compose(to, "Your One Punch HRIS account is closed",
            "Your account is now read-only, as you asked. You can still view and export your data until " +
            $"{Ph(purgeAfterUtc)}. After that your company records are removed from the live system. Records the law requires us to keep are held in a read-only archive, as described in our Terms of Service. " +
            "If you pay for a plan again before that date, your account is restored.");

    public static SendEmailRequested? Rejected(string? to, string reason) =>
        Compose(to, "About your account closure request",
            $"We couldn't close your account yet: {reason}. Your account is unchanged. Reply to this email or contact {Support} if you'd like to talk it through.");

    private static SendEmailRequested? Compose(string? to, string subject, string paragraph, params (string Label, string Url)[] links)
    {
        var address = to?.Trim();
        if (string.IsNullOrEmpty(address)) return null;

        var text = paragraph + string.Concat(links.Select(l => $"\n\n{l.Label}: {l.Url}")) + $"\n\nQuestions? Contact {Support}.";
        var html = $"<p>{WebUtility.HtmlEncode(paragraph)}</p>"
            + string.Concat(links.Select(l => $"<p><a href=\"{WebUtility.HtmlEncode(l.Url)}\">{WebUtility.HtmlEncode(l.Label)}</a></p>"))
            + $"<p>Questions? Contact <a href=\"mailto:{Support}\">{Support}</a>.</p>";
        return new SendEmailRequested { To = address, Subject = subject, HtmlBody = html, TextBody = text, Source = Source };
    }

    // The Philippines has no daylight saving time, so UTC+8 is always right.
    private static string Ph(DateTime utc) => $"{utc.AddHours(8):MMMM d, yyyy h:mm tt} (Philippine time)";
}
