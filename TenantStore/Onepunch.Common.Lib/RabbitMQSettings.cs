namespace Onepunch.Common.Lib;

public class RabbitMQSettings
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Exchange { get; set; }
    public string OnepunchQue { get; set; }
}

public class CryptoSetting
{
    public string AES_KEY { get; set; }
    public string AES_IV { get; set; }
}