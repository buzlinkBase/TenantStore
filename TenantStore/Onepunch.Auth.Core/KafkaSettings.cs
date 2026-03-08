namespace Onepunch.Auth.Core;

public class RabbitMqSettings
{
    public string Host { get; set; }
    public string VirtualHost { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    //public int Port { get; set; }
    //public string Exchange { get; set; }
    //public string HrmsQue { get; set; }
}

//public class KafkaSettings
//{
//    public string BootstrapServers { get; set; }
//    public TopicSettings Topics { get; set; }
//}

//public class TopicSettings
//{
//    public string TenantCreated { get; set; }
//    public string UserCreated { get; set; }
//    public string TenantUserConfirmed { get; set; }
//    public string SendUserInvitation { get; set; }
//    public string InvitedUserConfirmed { get; set; }
//} 