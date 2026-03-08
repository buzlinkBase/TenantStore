
namespace TenantStoreApi.Domain.Entities;

public class Tenant : BaseEntity
{
    public Guid AccountId { get; set; }
    public required string CompanyName { get; set; }
    public required string Email { get; set; }
    public string ApiToken { get; set; } = Guid.NewGuid().ToString();
    public Guid UserId { get; set; }
    //public ICollection<ApiToken> ResourceTokens { get; set; }
    //public ICollection<Subscription>     Subscriptions   { get; set; }
}
