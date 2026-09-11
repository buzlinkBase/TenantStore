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