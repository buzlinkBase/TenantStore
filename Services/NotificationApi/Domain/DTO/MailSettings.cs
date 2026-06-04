namespace OnePunch.Notification.Domain.DTO;

public class MailSettings
{
    public string From { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SmtpServer { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 2525;
}

public record AccountConfirmation
{
    public string Email { get; set; } = string.Empty;
    public string ConfirmationLink { get; set; } = string.Empty;
}
public record AccountConfirmationPayload
{
    public string Email { get; set; } = string.Empty;
    public string Name  { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public string InviteLink { get; set; } = string.Empty;
}