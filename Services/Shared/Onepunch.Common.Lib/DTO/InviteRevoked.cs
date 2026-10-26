namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by Tenant Service when an admin revokes or removes a still-pending invite
/// (the Status="Invited" placeholder membership), so Auth Service can expire the invitation
/// link right away instead of leaving it redeemable until it times out.
/// </summary>
public record InviteRevoked
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
