namespace Onepunch.Common.Lib;

public class HMacSetting
{
    public string SecretKey { get; set; }
    public string AppName { get; set; }
    public override string ToString()
    {
        return string.Concat(SecretKey, AppName);
    }
}

public class ApiKeySetting
{
    public string ApiKey { get; set; }
}
