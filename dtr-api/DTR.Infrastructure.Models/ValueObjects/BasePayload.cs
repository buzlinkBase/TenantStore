namespace DTR.Models.ValueObjects;

public class BasePayload
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
