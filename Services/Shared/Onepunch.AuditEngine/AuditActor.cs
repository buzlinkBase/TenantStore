namespace Onepunch.AuditEngine;

/// <summary>Who performed the action.</summary>
/// <param name="Id">A stable identifier (user id, service name, ...).</param>
/// <param name="DisplayName">What a reader of the trail sees, e.g. the user's name or email.</param>
/// <param name="Type">See <see cref="AuditActorTypes"/>.</param>
public sealed record AuditActor(string Id, string? DisplayName, string Type)
{
    public static AuditActor User(string id, string? displayName) => new(id, displayName, AuditActorTypes.User);

    /// <summary>Background work (scheduled jobs, message consumers) acting on its own behalf.</summary>
    public static AuditActor System(string name) => new(name, name, AuditActorTypes.System);

    public static readonly AuditActor Anonymous = new("anonymous", "Anonymous", AuditActorTypes.Anonymous);
}

public static class AuditActorTypes
{
    public const string User = "User";
    public const string System = "System";
    public const string Service = "Service";
    public const string Anonymous = "Anonymous";
}
