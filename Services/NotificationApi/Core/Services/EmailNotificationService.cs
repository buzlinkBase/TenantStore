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
        string relativePath = Path.Combine("Core", "Templates", "AccountConfirmation.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        var message = new EmailMessage
        {
            From = "Support <support@onepunch.site>",
            To = payload.Email,
            Subject = "Confirm your account",
            Template = new EmailMessageTemplate
            {
                TemplateId = "account-confirmation",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "name", payload.FullName ?? payload.Email },
                    { "confirmationUrl", payload.ConfirmationRoute },
                    { "expirationTime", "24 hours" }
                }
            }
        };
        await _resend.EmailSendAsync(message, token);
    }
    public async Task SendUserInvites(UserInvitionNotificationPayload payload, CancellationToken token)
    {
        var message = new EmailMessage
        {
            From = "User Invitation <userinvitation@onepunch.site>",
            To = payload.Email,
            Subject = "{{OrganizationName}} Invitation",
            Template = new EmailMessageTemplate
            {
                TemplateId = "user-invitation",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "OrganizationName",  payload.Organization },
                    { "InviteLink", payload.InviteLink},
                    { "Name", payload.Name ?? payload.Email},
                    { "ExpirationDateTime",  payload.Expiry.ToString()},
                    { "CurrentYear", DateTime.UtcNow.Year.ToString()},
                }
            }
        };
        await _resend.EmailSendAsync(message, token);

    }
    public async Task SendSignInGoogleInformation(SignInGoogleEmail payload, CancellationToken token)
    {
        var message = new EmailMessage
        {
            From = "Reset Password <resetpassword@onepunch.site>",
            To = payload.Email,
            Subject = "Reset Password Request",
            Template = new EmailMessageTemplate
            {
                TemplateId = "google-resetpassword",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "LoginLink", payload.LoginLink},
                    { "UserName", payload.Name ?? payload.Email},
                    { "CurrentYear", DateTime.UtcNow.Year.ToString()},
                }
            }
        };
        await _resend.EmailSendAsync(message, token);

    }
    public async Task SendResetPassword(ResetPasswordEmail payload, CancellationToken token)
    {
        var message = new EmailMessage
        {
            From = "Reset Password <resetpassword@onepunch.site>",
            To = payload.Email,
            Subject = "Reset Password Request",
            Template = new EmailMessageTemplate
            {
                TemplateId = "password-reset",
                Variables = new Dictionary<string, object>
                {
                    { "appName", "Onepunch" },
                    { "ResetLink", payload.ResetLink},
                    { "UserName", payload.Name ?? payload.Email },
                    { "CurrentYear", DateTime.UtcNow.Year.ToString() },
                    { "ExpirationDateTime", payload.Expiry.ToString()}
                }
            }
        };
        await _resend.EmailSendAsync(message, token);

    }

}
