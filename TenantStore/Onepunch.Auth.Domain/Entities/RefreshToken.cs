using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

public class RefreshToken : BaseEntity 
{
    public Guid UserId { get; set; }
    public string RefreshTokenHash { get; set; }
    public DateTime Expiry { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool Revoked { get; set; } = false;
}