
using BuzlinkRepository;
using OnePunch.Auth.Domain.Entities;
namespace Onepunch.Auth.Domain.Entities;

public class EmailToken : BaseEntity, IEntityTenant
{
    public string Email  { get; set; }
    public Guid? UserId { get; set; }
    public string TokenType { get; set; }  
    public string TokenValue { get; set; }
    public bool IsUsed { get; set; }
    public DateTime Expiry  { get; set; }
    public Guid TenantId { get; set; }
}
 