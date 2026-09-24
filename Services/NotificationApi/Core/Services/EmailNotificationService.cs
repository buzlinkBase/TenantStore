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
        if (!payload.DeliverEmail) return;

        var recipientName = string.IsNullOrWhiteSpace(payload.RecipientName) ? payload.RecipientEmail : payload.RecipientName;
        var subject = payload.StatusLabel == "Pending Your Approval"
            ? $"{payload.ApplicationTypeLabel} application awaiting your approval"
            : $"Your {payload.ApplicationTypeLabel} application was {payload.StatusLabel.ToLowerInvariant()}";

        var message = new EmailMessage
        {
            From = "Onepunch <approvals@onepunch.site>",
            To = payload.RecipientEmail,
            Subject = subject,
            Template = new EmailMessageTemplate
            {
                TemplateId = "approval-notification",
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
        };

        await _resend.EmailSendAsync(message, token);
    }

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