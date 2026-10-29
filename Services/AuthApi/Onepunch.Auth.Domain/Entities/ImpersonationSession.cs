namespace Onepunch.Auth.Domain.Entities;

/// <summary>
/// One support operator signing in as a company user. Created when the operator asks (with a
/// reason), redeemed once through <see cref="ExchangeCodeHash"/> for the access token, and ended by
/// Stop or by <see cref="ExpiresAtUtc"/>. Never deleted: it is the record of who was inside whose
/// account, why, and for how long.
/// </summary>
public class ImpersonationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAtUtc { get; set; }
    /// <summary>The session's hard end; the access token never outlives it and is never refreshed.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    public Guid OperatorId { get; set; }
    /// <summary>Kept as text so the record survives the operator being renamed or removed.</summary>
    public string OperatorEmail { get; set; } = string.Empty;
    public string OperatorRole { get; set; } = string.Empty;

    public Guid TargetUserId { get; set; }
    public string TargetEmail { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>SHA-256 of the one-time code handed to the console; the code itself is never stored.</summary>
    public string ExchangeCodeHash { get; set; } = string.Empty;
    public DateTime ExchangeCodeExpiresAtUtc { get; set; }
    public DateTime? ExchangedAtUtc { get; set; }
}

public static class ImpersonationActions
{
    public const string Started = "impersonation.started";
    public const string Stopped = "impersonation.stopped";
}
