using System.Net;
using System.Text;

namespace OnePunch.Notification.Core.Services;

/// <summary>
/// Subject and body for each subscription email (trial reminders, payment, suspension, purge
/// warnings). Composed in code, since there are no designed Resend templates for these yet.
/// Pure and static: payload in, email content out. Dates show in Philippine time.
/// </summary>
public static class SubscriptionEmailComposer
{
    private const string BrandColor = "#1DA081";

    public sealed record SubscriptionEmailContent(string Subject, string HtmlBody, string TextBody);

    private static readonly TimeZoneInfo Manila = ResolveManila();

    public static SubscriptionEmailContent? Compose(SubscriptionReminderEmail p)
    {
        var plan = string.IsNullOrWhiteSpace(p.PlanName) ? "Onepunch" : p.PlanName;
        (string Subject, string[] Paragraphs)? content = p.Template switch
        {
            SubscriptionEmailTemplates.TrialWelcome => (
                "Welcome to Onepunch -- your free trial has started",
                [
                    $"Your trial gives you everything in {plan}, free until {Date(p.TrialEndsAtUtc)}. No payment details needed.",
                    "Get started: add your employees, connect your biometric devices, set up time shifts, then generate your first DTR.",
                ]),
            SubscriptionEmailTemplates.TrialUsageSummary => (
                "3 days left in your Onepunch trial",
                [
                    $"Your trial ends on {Date(p.TrialEndsAtUtc)}.",
                    UsageLine(p),
                    "Choose a plan any time from Billing to keep everything running without interruption.",
                ]),
            SubscriptionEmailTemplates.TrialEndsTomorrow => (
                "Your Onepunch trial ends tomorrow",
                [
                    $"Your trial ends on {Date(p.TrialEndsAtUtc)}. After that your companies become read-only: you can still sign in, view and export, but not make changes, and employees can't file or time in from the web.",
                    "Choose a plan from Billing today to avoid the interruption.",
                ]),
            SubscriptionEmailTemplates.TrialEnded => (
                "Your Onepunch trial has ended",
                [
                    "Your trial ended today and your companies are now read-only. Nothing has been deleted.",
                    "Choose a plan from Billing to pick up where you left off.",
                ]),
            SubscriptionEmailTemplates.TrialExpiredReminder => (
                "Your Onepunch data is still here",
                [
                    "Your trial has ended, but everything you set up is safe and waiting.",
                    "Choose a plan from Billing to continue.",
                ]),
            SubscriptionEmailTemplates.PaymentDue => (
                $"Your {plan} subscription renews on {Date(p.PeriodEndUtc)}",
                [
                    $"Your current period ends on {Date(p.PeriodEndUtc)}.",
                    "Sign in and open Billing for the amount due and how to pay.",
                ]),
            SubscriptionEmailTemplates.PastDue => (
                "Payment overdue for your Onepunch subscription",
                [
                    $"We haven't received payment for your {plan} subscription. You keep full access until {Date(p.GraceEndsAtUtc)}; after that your companies become read-only.",
                    "Open Billing to settle it.",
                ]),
            SubscriptionEmailTemplates.Suspended => (
                "Your Onepunch subscription is suspended",
                [
                    "Payment wasn't received, so your companies are now read-only. You can still sign in, view and export. Nothing has been deleted.",
                    "Open Billing to pay and restore full access.",
                ]),
            SubscriptionEmailTemplates.Canceled => (
                "Your Onepunch subscription has ended",
                [
                    "Your subscription ended and your companies are now read-only.",
                    p.PurgeAfterUtc != null
                        ? $"Your data stays in your account until {Date(p.PurgeAfterUtc)}. Choose a plan before then to keep it."
                        : "Choose a plan from Billing to continue.",
                ]),
            SubscriptionEmailTemplates.PurgeWarning => (
                $"Your Onepunch data will be removed from your account on {Date(p.PurgeAfterUtc)}",
                [
                    $"Your companies are read-only, and on {Date(p.PurgeAfterUtc)} their data will be removed from your account.",
                    "Payroll, employee, attendance and tax records are moved to a secure archive and kept for the period the law requires. Everything else, such as notifications, announcements and uploaded files, is deleted.",
                    "Choose a plan from Billing before then to keep everything as it is, or export what you need now.",
                ]),
            _ => null,
        };
        if (content is not { } c) return null;

        var recipient = string.IsNullOrWhiteSpace(p.FullName) ? p.Email : p.FullName;
        var paragraphs = c.Paragraphs.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return new SubscriptionEmailContent(c.Subject, BuildHtml(recipient, paragraphs), BuildText(recipient, paragraphs));
    }

    private static string UsageLine(SubscriptionReminderEmail p) =>
        (p.CompanyCount, p.EmployeeCount) switch
        {
            ({ } companies, { } employees) =>
                $"So far you've set up {companies} {(companies == 1 ? "company" : "companies")} with {employees} {(employees == 1 ? "employee" : "employees")}.",
            _ => "",
        };

    /// <summary>A UTC date as a Philippine calendar date, e.g. "October 9, 2026".</summary>
    public static string Date(DateTime? utc) =>
        utc is { } value
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), Manila).ToString("MMMM d, yyyy")
            : "soon";

    private static TimeZoneInfo ResolveManila()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("PHT", TimeSpan.FromHours(8), "Philippine Time", "Philippine Time");
    }

    private static string BuildHtml(string recipient, string[] paragraphs)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var body = new StringBuilder();
        foreach (var paragraph in paragraphs)
            body.Append($"<p style=\"margin:0 0 16px;font-size:15px;color:#111827\">{E(paragraph)}</p>");

        return $"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:24px;background:#f5f7f6;font-family:Arial,Helvetica,sans-serif">
              <table role="presentation" width="100%" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;border-top:4px solid {BrandColor}">
                <tr><td style="padding:24px">
                  <p style="margin:0 0 4px;font-size:12px;letter-spacing:1px;color:{BrandColor};font-weight:bold">ONEPUNCH</p>
                  <p style="margin:0 0 16px;color:#111827">Hi {E(recipient)},</p>
                  {body}
                  <p style="margin:24px 0 0;font-size:12px;color:#9ca3af">You're receiving this because you own a Onepunch account. &copy; {DateTime.UtcNow.Year} Onepunch</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildText(string recipient, string[] paragraphs)
    {
        var text = new StringBuilder().AppendLine($"Hi {recipient},").AppendLine();
        foreach (var paragraph in paragraphs) text.AppendLine(paragraph).AppendLine();
        return text.AppendLine("You're receiving this because you own a Onepunch account.").ToString();
    }
}
