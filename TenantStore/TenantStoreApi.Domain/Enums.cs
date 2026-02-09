namespace TenantStoreApi.Domain;

public enum TokenStatus
{
    Active,
    Expired,
    Revoke,
}

public enum TokenType
{
    API,
    Resource
}

public enum TokenExpirationType
{
    None,
    X1Mos,
    X6Mos, 
    X12Mos, 
}
