using Microsoft.Extensions.Options;
using OnePunch.Notification.Domain.DTO;
using RTools_NTS.Util;
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
    public async Task SendAccountConfirmation(MailPayload payload, string redirectSiteLink, CancellationToken token)
    {
        string relativePath = Path.Combine("Core", "Templates", "AccountConfirmation.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{confirmationLink}}", redirectSiteLink)
            ;
        MailMessage mail = new MailMessage();
        mail.To.Add(payload.ToMail);
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

    public async Task SendUserInvites(MailPayload payload, UserEmailPayload model, CancellationToken token)
    {
        string relativePath = Path.Combine("Core", "Templates", "UserInvitation.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = await File.ReadAllTextAsync(filePath, token);
        body = body
            .Replace("{{InviteLink}}", model.ConfirmationRoute)
            .Replace("{{OrganizationName}}", model.TenantName ?? "")
            .Replace("{{UserName}}", model.FullName ?? model.Email)
            .Replace("{{ExpirationDateTime}}", model.Expiry.ToString())
            .Replace("{{token}}", payload.Token)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;
        MailMessage mail = new MailMessage();
        mail.To.Add(payload.ToMail);
        mail.From = new MailAddress(_setting.From);
        mail.Subject = string.Concat(model.TenantName, "'s ", "invited you to ", !string.IsNullOrWhiteSpace(model.AppName) ? model.AppName : "Erp system");
        mail.Body = body;
        mail.IsBodyHtml = true;
        var client = new SmtpClient(_setting.SmtpServer, 2525)
        {
            Credentials = new NetworkCredential(_setting.Username, _setting.Password),
            EnableSsl = true
        };
        await client.SendMailAsync(mail, token);
    }

    public async Task SendResetPassword(MailPayload payload, ResetPasswordEmail model, CancellationToken token)
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
            .Replace("{{token}}", payload.Token)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;
        MailMessage mail = new MailMessage();
        mail.To.Add(payload.ToMail);
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
