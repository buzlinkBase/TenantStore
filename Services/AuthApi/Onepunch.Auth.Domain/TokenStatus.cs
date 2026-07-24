namespace Onepunch.Auth.Domain;

public enum TokenStatus
{
    Active,
    Expired,
    Revoke,
}

public enum TokenType
{
    Api,
    Resource
}

public enum TokenExpirationType
{
    None,
    X1Mos,
    X6Mos,
    X12Mos,
}
