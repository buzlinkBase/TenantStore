namespace OnePunch.Notification.Core;

public class KafkaSettings
{
    public string BootstrapServers { get; set; }
    public TopicSettings Topics { get; set; }
}
public class TopicSettings
{
    public string TenantCreated { get; set; }
    public string UserCreated { get; set; }
    public string TenantUserConfirmed { get; set; }
    public string SendUserInvitation { get; set; }
    public string InvitedUserConfirmed { get; set; }
}