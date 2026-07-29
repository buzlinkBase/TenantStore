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
        //string body = await File.ReadAllTextAsync(filePath, token);
        //body = body
        //    .Replace("{{confirmationLink}}", payload.ConfirmationRoute)
        //    ;
        //var message = new EmailMessage();
        //message.From = _setting.From;
        //message.To.Add(payload.Email);
        //message.Subject = "Confirm your account";
        //message.HtmlBody = body;

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
    public async Task SendUserInvites(UserInvitionNotificationPayload model, CancellationToken token)
    {
        string relativePath = Path.Combine("Core", "Templates", "UserInvitation.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }

        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{InviteLink}}", model.InviteLink)
            .Replace("{{OrganizationName}}", model.Organization ?? "")
            .Replace("{{Name}}", model.Name ?? model.Email)
            .Replace("{{ExpirationDateTime}}", model.Expiry.ToString())
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;

        var message = new EmailMessage();
        message.From = _setting.From;
        message.To.Add(model.Email);
        message.Subject = string.Concat(model.Organization, "'s", " ", "Invitation");
        message.HtmlBody = body;
        await _resend.EmailSendAsync(message, token);
    }
    public async Task SendSignInGoogleInformation(SignInGoogleEmail model, CancellationToken token)
    {
        string relativePath = Path.Combine("Core", "Templates", "SignInGoogleEmail.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{LoginLink}}", model.LoginLink)
            .Replace("{{UserName}}", model.Name ?? model.Email)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;
        var message = new EmailMessage();
        message.From = _setting.From;
        message.To.Add(model.Email);
        var displayName = model?.Name ?? "";
        message.Subject = $"Request password reset {(displayName.Length > 100 ? displayName[..100] : displayName)}";
        message.HtmlBody = body;
        await _resend.EmailSendAsync(message, token);
    }
    public async Task SendResetPassword(ResetPasswordEmail model, CancellationToken token)
    {
        string relativePath = Path.Combine("Core", "Templates", "ResetPassword.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{ResetLink}}", model.ResetLink)
            .Replace("{{UserName}}", model.Name ?? model.Email)
            .Replace("{{ExpirationDateTime}}", model.Expiry.ToString())
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;
        var message = new EmailMessage();
        message.From = _setting.From;
        message.To.Add(model.Email);
        var displayName = model?.Name ?? "";
        message.Subject = $"Request password reset {(displayName.Length > 100 ? displayName[..100] : displayName)}";
        message.HtmlBody = body;
        await _resend.EmailSendAsync(message, token);
    }

}
