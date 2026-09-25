using System.Net;
using System.Text;

namespace OnePunch.Notification.Core.Services;

/// <summary>
/// Subject line for every approval email, plus a code-composed HTML/plain-text body used only
/// as a FALLBACK: the primary send uses the designed "approval-notification" Resend template
/// (see EmailNotificationService.SendApprovalNotification). If Resend reports that template
/// missing (still a Draft, or the API key is on another Resend team), this body goes out
/// instead so the email isn't lost. Pure and static: payload in, email content out.
/// </summary>
public static class ApprovalEmailComposer
{
    // Mirrors hrms-api's ApprovalEngineService status labels.
    public const string PendingYourApproval = "Pending Your Approval";
    public const string StepApproved = "Step Approved";

    private const string BrandColor = "#1DA081";

    public sealed record ApprovalEmailContent(string Subject, string HtmlBody, string TextBody);

    public static ApprovalEmailContent Compose(ApprovalNotificationRequested payload)
    {
        var type = payload.ApplicationTypeLabel;
        var recipient = string.IsNullOrWhiteSpace(payload.RecipientName) ? payload.RecipientEmail : payload.RecipientName;
        var stepText = payload is { StepNumber: { } step, TotalSteps: { } total } ? $"step {step} of {total}" : null;

        var (subject, headline) = payload.StatusLabel switch
        {
            PendingYourApproval => (
                $"{type} application awaiting your approval",
                $"{payload.ApplicantName}'s {type} application is waiting on you" + (stepText != null ? $" ({stepText})." : ".")),
            StepApproved => (
                stepText != null ? $"Your {type} application passed {stepText}" : $"Your {type} application moved forward",
                $"Your {type} application passed {stepText ?? "a step"} and moved to the next approver."),
            _ => (
                $"Your {type} application was {payload.StatusLabel.ToLowerInvariant()}",
                $"Your {type} application was {payload.StatusLabel.ToLowerInvariant()}."),
        };

        var note = string.IsNullOrWhiteSpace(payload.Note) ? null : payload.Note.Trim();

        return new ApprovalEmailContent(
            subject,
            BuildHtml(recipient, headline, payload, stepText, note),
            BuildText(recipient, headline, payload, stepText, note));
    }

    private static string BuildHtml(string recipient, string headline, ApprovalNotificationRequested p, string? stepText, string? note)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var rows = new StringBuilder();
        void Row(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            rows.Append($"<tr><td style=\"padding:4px 12px 4px 0;color:#6b7280\">{E(label)}</td>")
                .Append($"<td style=\"padding:4px 0;color:#111827\">{E(value)}</td></tr>");
        }
        Row("Application", p.ApplicationTypeLabel);
        Row("Applicant", p.ApplicantName);
        Row("Status", p.StatusLabel);
        Row("Step", stepText);
        Row("Note", note);

        return $"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:24px;background:#f5f7f6;font-family:Arial,Helvetica,sans-serif">
              <table role="presentation" width="100%" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;border-top:4px solid {BrandColor}">
                <tr><td style="padding:24px">
                  <p style="margin:0 0 4px;font-size:12px;letter-spacing:1px;color:{BrandColor};font-weight:bold">ONEPUNCH</p>
                  <p style="margin:0 0 16px;color:#111827">Hi {E(recipient)},</p>
                  <p style="margin:0 0 16px;font-size:16px;color:#111827">{E(headline)}</p>
                  <table role="presentation" style="font-size:14px">{rows}</table>
                  <p style="margin:24px 0 0;font-size:12px;color:#9ca3af">Sign in to Onepunch to view the full approval history. &copy; {DateTime.UtcNow.Year} Onepunch</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildText(string recipient, string headline, ApprovalNotificationRequested p, string? stepText, string? note)
    {
        var text = new StringBuilder()
            .AppendLine($"Hi {recipient},")
            .AppendLine()
            .AppendLine(headline)
            .AppendLine()
            .AppendLine($"Application: {p.ApplicationTypeLabel}")
            .AppendLine($"Applicant: {p.ApplicantName}")
            .AppendLine($"Status: {p.StatusLabel}");
        if (stepText != null) text.AppendLine($"Step: {stepText}");
        if (note != null) text.AppendLine($"Note: {note}");
        return text
            .AppendLine()
            .AppendLine("Sign in to Onepunch to view the full approval history.")
            .ToString();
    }
}
