using OnePunch.Auth.Domain.Entities;
namespace Onepunch.Auth.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string RefreshTokenHash { get; set; }
    public DateTime Expiry { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool Revoked { get; set; } = false;

    /// <summary>
    /// The company this session is working in. A refresh re-mints for it rather than for the
    /// account's DefaultTenantId, which is account-wide -- switching company on one device must
    /// not move every other signed-in device along with it. Null on tokens issued before this
    /// existed; those refresh into the default company, as they always did.
    /// </summary>
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
}
