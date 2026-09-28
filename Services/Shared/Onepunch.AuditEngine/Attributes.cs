namespace Onepunch.AuditEngine;

/// <summary>
/// Opts an entity into automatic create/update/delete capture. Capture is curated, not blanket:
/// only entities a host marks are recorded, so bulk/derived tables (computed DTRs, ledgers,
/// caches) don't flood the trail.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AuditedAttribute : Attribute
{
    /// <summary>Name recorded as <see cref="AuditEvent.EntityType"/>; defaults to the class name.</summary>
    public string? EntityType { get; init; }
}

/// <summary>Leaves a property out of recorded changes (noise, blobs, or values never worth showing).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;
