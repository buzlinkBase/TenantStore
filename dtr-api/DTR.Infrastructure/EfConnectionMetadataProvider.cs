namespace DTR.Infrastructure;
public interface IDbConnectionProvider
{
    string? GetConnectionString(Guid tenantId);
} 

public class DbConnectionProvider : IDbConnectionProvider
{
    public string? GetConnectionString(Guid tenantId)
    {
        return  null;
    }
}