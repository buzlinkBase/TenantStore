 
using OnePunch.Auth.Domain.Entities;
namespace Onepunch.Auth.Domain.Entities;

public class EmailToken : BaseEntity
{
    //public Guid EventId { get; set; }
    //public Guid? CausationId { get; set; }
    //public Guid? CorrelationId  { get; set; }
    //public  string EventType { get; set; }
    public string Email  { get; set; }
    public Guid? UserId { get; set; }
    public string TokenType { get; set; }  
    public string TokenValue { get; set; }
    public bool IsUsed { get; set; }
    public DateTime Expiry  { get; set; }
}
 