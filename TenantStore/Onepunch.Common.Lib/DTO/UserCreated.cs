namespace Onepunch.Common.Lib.DTO;

public record UserCreated
{
    public required string CompanyName { get; set; }
    public Guid UserId { get; set; }
}

 