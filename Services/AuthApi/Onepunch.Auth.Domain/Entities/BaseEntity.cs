
using BuzlinkRepository;
using Mapster;

namespace OnePunch.Auth.Domain.Entities;

public abstract class BaseEntity : EntityBase, ITimeStamp
{
    [AdaptIgnore]
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [AdaptIgnore]
    public DateTime? DeletedAt { get; set; }
}
