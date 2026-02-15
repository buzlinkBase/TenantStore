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

public class CryptoSetting
{
    public string AES_KEY { get; set; }
    public string AES_IV { get; set; }
}