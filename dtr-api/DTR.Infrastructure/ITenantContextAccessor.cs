namespace DTR.Infrastructure;

public interface ITenantContextAccessor
{
    Guid GetTenantId();
}
public interface IAppConfigurationProvider
{
    string? GetConnectionString(string name);
}