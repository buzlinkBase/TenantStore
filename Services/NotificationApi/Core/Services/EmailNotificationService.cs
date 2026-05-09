using Microsoft.Extensions.Options;
using OnePunch.Notification.Domain.DTO;
using System.Net;
using System.Net.Mail;

namespace OnePunch.Notification.Core.Services;

public class EmailNotificationService
{
    private readonly MailSettings _setting;
    public EmailNotificationService(IOptions<MailSettings> setting)
    {
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
        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{confirmationLink}}", payload.ConfirmationRoute)
            ;
        MailMessage mail = new MailMessage();
        mail.To.Add(payload.Email);
        mail.From = new MailAddress(_setting.From);
        mail.Subject = "Confirm your account";
        mail.Body = body;
        mail.IsBodyHtml = true;
        var client = new SmtpClient(_setting.SmtpServer, 2525)
        {
            Credentials = new NetworkCredential(_setting.Username, _setting.Password),
            EnableSsl = true
        };
        await client.SendMailAsync(mail, token);
    }
    public async Task SendUserInvites( UserInvitionNotificationPayload model, CancellationToken token)
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

        MailMessage mail = new MailMessage();
        mail.To.Add(model.Email);
        mail.From = new MailAddress(_setting.From);
        mail.Subject = string.Concat(model.Organization, "'s", " ", "Invitation");
        mail.Body = body;
        mail.IsBodyHtml = true;

        var client = new SmtpClient(_setting.SmtpServer, 2525)
        {
            Credentials = new NetworkCredential(_setting.Username, _setting.Password),
            EnableSsl = true
        };
        await client.SendMailAsync(mail, token);
    }
    public async Task SendResetPassword(ResetPasswordEmail model, CancellationToken token)
    {
        string relativePath = Path.Combine("Core", "Templates", "ResetPasssord.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{InviteLink}}", model.ResetLink)
            .Replace("{{UserName}}", model.Name ?? model.Email)
            .Replace("{{ExpirationDateTime}}", model.Expiry.ToString())
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;
        MailMessage mail = new MailMessage();
        mail.To.Add(model.Email);
        mail.From = new MailAddress(_setting.From);
        mail.Subject = $"Request password reset {(model?.Name ?? "").Substring(0, 100)}";
        mail.Body = body;
        mail.IsBodyHtml = true;
        var client = new SmtpClient(_setting.SmtpServer, 2525)
        {
            Credentials = new NetworkCredential(_setting.Username, _setting.Password),
            EnableSsl = true
        };
        await client.SendMailAsync(mail, token);
    }
}
