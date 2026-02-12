namespace OnePunch.Notification.Core;

public class KafkaSettings
{
    public string BootstrapServers { get; set; }
    public TopicSettings Topics { get; set; }
}

public class TopicSettings
{
    public string UserCreated { get; set; }
}