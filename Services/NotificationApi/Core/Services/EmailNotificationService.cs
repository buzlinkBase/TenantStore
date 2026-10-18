using Microsoft.Extensions.Options;
using Resend;

namespace OnePunch.Notification.Core.Services;

public class EmailNotificationService
{
    private readonly IResend _resend;
    private readonly ResendSettings _setting;

    public EmailNotificationService(IResend resend, IOptions<ResendSettings> setting)
    {
        _resend = resend;
        _setting = setting.Value;
    }

    public async Task SendAccountConfirmation(SendAccountVerification payload, CancellationToken token)
    {
        var recipientName = string.IsNullOrWhiteSpace(payload.FullName) ? payload.Email : payload.FullName;

        var message = new EmailMessage
        {
            From = "Onepunch <support@onepunch.site>",
            To = payload.Email,
            Subject = "Confirm your account",
            Template = new EmailMessageTemplate
            {
                TemplateId = "account-confirmation",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "name", recipientName },
                    { "confirmationUrl", payload.ConfirmationRoute },
                    { "expirationTime", "24 hours" }
                }
            }
        };

        await _resend.EmailSendAsync(message, token);
    }

    public async Task SendUserInvites(UserInvitionNotificationPayload payload, CancellationToken token)
    {
        var formattedExpiry = payload.Expiry.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var recipientName = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name;

        var message = new EmailMessage
        {
            From = "Onepunch <userinvitation@onepunch.site>",
            To = payload.Email,
            Subject = $"You've been invited to join {payload.Organization} on Onepunch",
            Template = new EmailMessageTemplate
            {
                TemplateId = "user-invitation",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "OrganizationName", payload.Organization },
                    { "InviteLink", payload.InviteLink },
                    { "Name", recipientName },
                    { "ExpirationDateTime", formattedExpiry },
                    { "CurrentYear", DateTime.UtcNow.Year.ToString() }
                }
            }
        };

        await _resend.EmailSendAsync(message, token);
    }

    public async Task SendSignInGoogleInformation(SignInGoogleEmail payload, CancellationToken token)
    {
        var recipientName = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name;

        var message = new EmailMessage
        {
            From = "Onepunch <support@onepunch.site>",
            To = payload.Email,
            Subject = "Google Sign-In Account Notice",
            Template = new EmailMessageTemplate
            {
                TemplateId = "google-resetpassword",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "LoginLink", payload.LoginLink },
                    { "UserName", recipientName },
                    { "CurrentYear", DateTime.UtcNow.Year.ToString() }
                }
            }
        };

        await _resend.EmailSendAsync(message, token);
    }

    public async Task SendApprovalNotification(ApprovalNotificationRequested payload, CancellationToken token)
    {
        // Recipient opted out of email for this application type (NotificationPreference,
        // resolved once in hrms-api's ApprovalEngineService) -- push may still be delivered
        // independently by hrms-api's own ApprovalPushNotificationWorker via DeliverPush.
        if (!payload.DeliverEmail || string.IsNullOrWhiteSpace(payload.RecipientEmail)) return;

        // Subject comes from the composer for both paths (it also covers "Step Approved").
        var content = ApprovalEmailComposer.Compose(payload);
        var recipientName = string.IsNullOrWhiteSpace(payload.RecipientName) ? payload.RecipientEmail : payload.RecipientName;

        try
        {
            // Primary: the designed "approval-notification" template in the Resend dashboard.
            await _resend.EmailSendAsync(new EmailMessage
            {
                From = ApprovalsSender,
                To = payload.RecipientEmail,
                Subject = content.Subject,
                Template = new EmailMessageTemplate
                {
                    TemplateId = ApprovalTemplateId,
                    Variables = new Dictionary<string, object>
                    {
                        { "appName", "Onepunch" },
                        { "recipientName", recipientName },
                        { "applicationType", payload.ApplicationTypeLabel },
                        { "applicantName", payload.ApplicantName },
                        { "statusLabel", payload.StatusLabel },
                        { "stepNumber", payload.StepNumber?.ToString() ?? "" },
                        { "totalSteps", payload.TotalSteps?.ToString() ?? "" },
                        { "note", payload.Note ?? "" },
                        { "CurrentYear", DateTime.UtcNow.Year.ToString() }
                    }
                }
            }, token);
        }
        catch (ResendException ex) when (ex.ErrorType == ErrorType.NotFound)
        {
            // Resend answers "Template not found" when the template is still a Draft (only
            // Published templates can send) or the API key belongs to a different Resend team
            // than the one holding it. Don't lose the email over that -- send the composed
            // version instead, and leave a trail so the template can be fixed. Any other Resend
            // error still throws, so MassTransit retries/faults as usual.
            Serilog.Log.Logger.Warning(ex,
                "Resend template {TemplateId} not found -- sent the composed fallback approval email to {Recipient} instead. Publish the template in Resend and check the API key's team.",
                ApprovalTemplateId, payload.RecipientEmail);

            await _resend.EmailSendAsync(new EmailMessage
            {
                From = ApprovalsSender,
                To = payload.RecipientEmail,
                Subject = content.Subject,
                HtmlBody = content.HtmlBody,
                TextBody = content.TextBody,
            }, token);
        }
    }

    private const string ApprovalsSender = "Onepunch <approvals@onepunch.site>";

    /// <summary>Trial, payment and suspension emails to an account owner (Tenant Service's
    /// subscription lifecycle job). An unknown template is logged and dropped, not retried.</summary>
    public async Task SendSubscriptionReminder(SubscriptionReminderEmail payload, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(payload.Email)) return;

        var content = SubscriptionEmailComposer.Compose(payload);
        if (content == null)
        {
            Serilog.Log.Logger.Warning("Unknown subscription email template {Template} for {Recipient}", payload.Template, payload.Email);
            return;
        }

        await _resend.EmailSendAsync(new EmailMessage
        {
            From = BillingSender,
            To = payload.Email,
            Subject = content.Subject,
            HtmlBody = content.HtmlBody,
            TextBody = content.TextBody,
        }, token);
    }

    /// <summary>
    /// Sends a message another service composed. The address is always ours; the sender only chooses the display name. A message that
    /// fails validation is dropped (retrying cannot fix it); a Resend failure throws so the bus retries.
    /// </summary>
    public async Task SendGeneralEmail(SendEmailRequested payload, CancellationToken token)
    {
        if (!SendEmailRequested.TryNormalize(payload, out var message, out var problem))
        {
            Serilog.Log.Logger.Warning("Dropped an email from {Source}: {Problem}", payload.Source, problem);
            return;
        }

        var email = new EmailMessage
        {
            From = $"{message.FromName ?? "Onepunch"} <support@onepunch.site>",
            To = message.To,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        };
        if (message.ReplyTo is not null)
            email.ReplyTo = message.ReplyTo;

        await _resend.EmailSendAsync(email, token);
    }

    private const string BillingSender = "Onepunch <support@onepunch.site>";
    private const string ApprovalTemplateId = "approval-notification";

    public async Task SendResetPassword(ResetPasswordEmail payload, CancellationToken token)
    {
        var formattedExpiry = payload.Expiry.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var recipientName = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name;

        var message = new EmailMessage
        {
            From = "Onepunch <resetpassword@onepunch.site>",
            To = payload.Email,
            Subject = "Reset Your Onepunch Password",
            Template = new EmailMessageTemplate
            {
                TemplateId = "password-reset",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "ResetLink", payload.ResetLink },
                    { "UserName", recipientName },
                    { "CurrentYear", DateTime.UtcNow.Year.ToString() },
                    { "ExpirationDateTime", formattedExpiry }
                }
            }
        };

        await _resend.EmailSendAsync(message, token);
    }
}