namespace Onepunch.Auth.Domain.DTOs;

public class AuthSettings
{
    public Frontends Frontends { get; set; }
    public Apis Apis { get; set; }
}

public class Frontends
{
    public string Bio { get; set; }
}

public class Apis
{
    public string Bio { get; set; }
}
