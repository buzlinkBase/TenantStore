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
    public void SendTenantConfirmation(MailPayload payload, string redirectSiteLink)
    {
        string relativePath = Path.Combine("Core", "Templates", "AccountConfirmation.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = File.ReadAllText(filePath);
        body = body
            .Replace("{{confirmationLink}}", redirectSiteLink)
            .Replace("{{token}}", payload.Token)
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
        client.Send(mail);
    }
    public void SendUserInvites(MailPayload payload, UserInvitationNotificationInfo model)
    {
        string relativePath = Path.Combine("Core", "Templates", "UserInvitation.html");
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Template not found at: {filePath}");
        }
        string body = File.ReadAllText(filePath);
        body = body
            .Replace("{{InviteLink}}", model.ConfirmationRoute)
            .Replace("{{OrganizationName}}", model.TenantName ?? "")
            .Replace("{{UserName}}", model.FullName ?? model.Email)
            .Replace("{{token}}", payload.Token)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString())
            ;
        MailMessage mail = new MailMessage();
        mail.To.Add(payload.ToMail);
        mail.From = new MailAddress(_setting.From);
        mail.Subject = string.Concat(model.TenantName,"'s ", "invited you to ", !string.IsNullOrWhiteSpace(model.AppName) ? model.AppName : "Erp system");
        mail.Body = body;
        mail.IsBodyHtml = true;

        var client = new SmtpClient(_setting.SmtpServer, 2525)
        {
            Credentials = new NetworkCredential(_setting.Username, _setting.Password),
            EnableSsl = true
        };
        client.Send(mail);
    }
}
