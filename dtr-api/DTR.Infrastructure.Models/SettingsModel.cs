using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTR.Models;

public class JwtSettings
{
    public string Issuer { get; set; }
    public List<string> Audience { get; set; }
    public string SigningKey { get; set; }
    public int TokenExpiry { get; set; } = 5;
    public int RefreshExpiry { get; set; } = 30;
}

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

