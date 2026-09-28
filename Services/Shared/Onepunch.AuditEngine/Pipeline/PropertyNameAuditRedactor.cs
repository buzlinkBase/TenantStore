namespace Onepunch.AuditEngine.Pipeline;

/// <summary>
/// Masks the values of configured property names (case-insensitive, on any entity), e.g.
/// "PasswordHash" or "BankAccountNo". The fact that the field changed is still recorded -- only
/// the values are hidden.
/// </summary>
public sealed class PropertyNameAuditRedactor(IEnumerable<string> propertyNames, string mask) : IAuditRedactor
{
    private readonly HashSet<string> _properties = new(propertyNames, StringComparer.OrdinalIgnoreCase);

    public string? Redact(string? entityType, string property, string? value) =>
        value != null && _properties.Contains(property) ? mask : value;
}

/// <summary>An actor resolver that always answers the same actor (the default for non-web hosts).</summary>
public sealed class FixedAuditActorResolver(AuditActor actor, string? organizationId = null) : IAuditActorResolver
{
    public AuditActor ResolveActor() => actor;

    public string? ResolveOrganizationId() => organizationId;
}
