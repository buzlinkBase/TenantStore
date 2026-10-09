namespace Onepunch.Auth.Domain.Entities;

/// <summary>
/// One change an operator (or the bootstrap) made to the console's operators: who, to whom, what.
/// Append-only. Written in the same save as the change itself, so an action and its record stand or fall together.
/// </summary>
public class PlatformAdminAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; }

    /// <summary>The operator who did it; null when it was the bootstrap (configuration), not a person.</summary>
    public Guid? ActorId { get; set; }
    /// <summary>Kept as text so the record survives the operator being renamed or removed.</summary>
    public string ActorEmail { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string? TargetEmail { get; set; }
    public string? Detail { get; set; }
}

public static class PlatformAdminActions
{
    public const string Created = "operator.created";
    public const string RoleChanged = "operator.role-changed";
    public const string Deactivated = "operator.deactivated";
    public const string Reactivated = "operator.reactivated";
    public const string PasswordReset = "operator.password-reset";
    public const string MfaReset = "operator.mfa-reset";
    public const string SessionsRevoked = "operator.sessions-revoked";
    /// <summary>The break-glass path: a SuperAdmin re-seeded from configuration.</summary>
    public const string Recovered = "operator.recovered";
}
