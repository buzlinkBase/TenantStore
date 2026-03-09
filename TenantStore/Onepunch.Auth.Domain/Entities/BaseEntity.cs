
using BuzlinkRepository;

namespace OnePunch.Auth.Domain.Entities;

public abstract class BaseEntity : EntityBase
{
    public string Status { get; set; } = "Active";
}
