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

        var textBody = $@"Hello {recipientName},

Welcome to Onepunch! Please confirm your account by visiting the link below:
{payload.ConfirmationRoute}

This confirmation link will expire in 24 hours.

© {DateTime.UtcNow.Year} Onepunch";

        var message = new EmailMessage
        {
            From = "Onepunch <support@onepunch.site>",
            To = payload.Email,
            Subject = "Confirm your account",
            TextBody = textBody,
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

        var textBody = $@"Hello {recipientName},

You have been invited to join {payload.Organization} on Onepunch.

To accept your invitation, copy and paste this link into your browser:
{payload.InviteLink}

This invitation will expire on: {formattedExpiry}.

© {DateTime.UtcNow.Year} Onepunch";

        var message = new EmailMessage
        {
            From = "Onepunch <userinvitation@onepunch.site>",
            To = payload.Email,
            Subject = $"You've been invited to join {payload.Organization} on Onepunch",
            TextBody = textBody,
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

        var textBody = $@"Hello {recipientName},

We received a request regarding your Onepunch account linked with Google Sign-In.

You can log in directly using this link:
{payload.LoginLink}

© {DateTime.UtcNow.Year} Onepunch";

        var message = new EmailMessage
        {
            From = "Onepunch <support@onepunch.site>",
            To = payload.Email,
            Subject = "Google Sign-In Account Notice",
            TextBody = textBody,
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

        var textBody = $@"Hello {recipientName},

We received a request to reset your Onepunch account password.

Click the link below to set a new password:
{payload.ResetLink}

This link will expire on: {formattedExpiry}.

If you did not request a password reset, you can safely ignore this email.

© {DateTime.UtcNow.Year} Onepunch";

        var message = new EmailMessage
        {
            From = "Onepunch <resetpassword@onepunch.site>",
            To = payload.Email,
            Subject = "Reset Your Onepunch Password",
            TextBody = textBody,
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