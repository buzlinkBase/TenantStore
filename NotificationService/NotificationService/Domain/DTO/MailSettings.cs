namespace OnePunch.Notification.Domain.DTO;

public record MailPayload(string ToMail,string Token);
public class MailSettings
{
    public string From { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string SmtpServer { get; set; }
}
