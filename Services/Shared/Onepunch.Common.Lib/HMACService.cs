using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Common.Lib;

public interface IHMACService
{
    string Sign(string payloadJson);
    bool Verify(string payloadJson, string signature);
}

public class HMACService : IHMACService
{
    private readonly HMacSetting _setting;
    public HMACService(IOptions<HMacSetting> setting)
    {
        _setting = setting.Value;
    }

    public string Sign(string payloadJson)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_setting.ToString()));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        string signature = Convert.ToBase64String(hash);
        return signature;
    }

    public bool Verify(string payloadJson, string signature)
    {
        return Sign(payloadJson) == signature;
    }
}

public class HMacInfo
{
    public static string GetHMacPayloadString(Guid appId)
    {
        var payloadObj = new
        {
            Appid = appId,
            ExpiryDays = 365,
            Issuer = "easyfs"
        };
        return JsonConvert.SerializeObject(payloadObj);
    }
}
