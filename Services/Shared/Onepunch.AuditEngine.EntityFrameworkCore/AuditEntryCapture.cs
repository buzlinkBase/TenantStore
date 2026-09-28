using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Onepunch.AuditEngine.EntityFrameworkCore;

/// <summary>
/// Turns the change tracker's pending changes to <see cref="AuditedAttribute"/> entities into
/// audit events (not yet enriched or hashed -- the write pipeline does that).
/// </summary>
internal static class AuditEntryCapture
{
    private static readonly ConcurrentDictionary<Type, AuditedAttribute?> AuditedTypes = new();
    private static readonly ConcurrentDictionary<PropertyInfo, bool> IgnoredProperties = new();

    public static List<AuditEvent> Capture(IEnumerable<EntityEntry> entries, AuditEngineOptions options)
    {
        var events = new List<AuditEvent>();
        foreach (var entry in entries)
        {
            if (entry.Entity is AuditEventRecord) continue;

            // Metadata.ClrType, not Entity.GetType(): with lazy-loading proxies the runtime type
            // is a generated subclass.
            var audited = AuditedTypes.GetOrAdd(entry.Metadata.ClrType,
                t => t.GetCustomAttribute<AuditedAttribute>(inherit: true));
            if (audited == null) continue;

            var auditEvent = entry.State switch
            {
                EntityState.Added when Captures(audited, AuditCapture.Create) =>
                    Build(entry, audited, AuditEventTypes.Create, AddedChanges(entry, options)),
                EntityState.Deleted when Captures(audited, AuditCapture.Delete) =>
                    Build(entry, audited, AuditEventTypes.Delete, []),
                EntityState.Modified when IsSoftDelete(entry, options) =>
                    Captures(audited, AuditCapture.Delete) ? Build(entry, audited, AuditEventTypes.Delete, []) : null,
                EntityState.Modified when Captures(audited, AuditCapture.Update) =>
                    ModifiedChanges(entry, options) is { Count: > 0 } changes
                        ? Build(entry, audited, AuditEventTypes.Update, changes)
                        : null,
                _ => null,
            };
            if (auditEvent != null) events.Add(auditEvent);
        }
        return events;
    }

    private static bool Captures(AuditedAttribute audited, AuditCapture kind) => (audited.Capture & kind) == kind;

    private static AuditEvent Build(EntityEntry entry, AuditedAttribute audited, string eventType, List<AuditChange> changes)
    {
        var entityType = audited.EntityType ?? entry.Metadata.ClrType.Name;
        var entityId = KeyOf(entry);
        return new AuditEvent
        {
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            Description = $"{Verb(eventType)} {entityType} {entityId}".TrimEnd(),
            Changes = changes,
        };
    }

    private static string Verb(string eventType) => eventType switch
    {
        AuditEventTypes.Create => "Created",
        AuditEventTypes.Update => "Updated",
        AuditEventTypes.Delete => "Deleted",
        _ => eventType,
    };

    private static string? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key == null) return null;
        return string.Join(",", key.Properties.Select(p => Format(entry.Property(p.Name).CurrentValue)));
    }

    private static List<AuditChange> AddedChanges(EntityEntry entry, AuditEngineOptions options) =>
        Recordable(entry, options)
            .Select(p => (p.Metadata.Name, Value: Format(p.CurrentValue)))
            .Where(p => p.Value != null)
            .Select(p => new AuditChange(p.Name, null, p.Value))
            .ToList();

    // Compared as values, not as text: a decimal read back from a fixed-scale column is
    // 1538.4600..., the same amount saved from a form is 1538.46 -- equal, but formatted
    // differently. Likewise a DateTime differing only in Kind. Neither is a change.
    private static List<AuditChange> ModifiedChanges(EntityEntry entry, AuditEngineOptions options) =>
        Recordable(entry, options)
            .Where(p => p.IsModified && !SameValue(p.OriginalValue, p.CurrentValue))
            .Select(p => new AuditChange(p.Metadata.Name, Format(p.OriginalValue), Format(p.CurrentValue)))
            .Where(c => c.OldValue != c.NewValue)
            .ToList();

    internal static bool SameValue(object? original, object? current) => (original, current) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (DateTime a, DateTime b) => a.Ticks == b.Ticks,
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        _ => original.Equals(current),
    };

    private static IEnumerable<PropertyEntry> Recordable(EntityEntry entry, AuditEngineOptions options) =>
        entry.Properties.Where(p =>
            p.Metadata.PropertyInfo is { } info &&
            !options.IgnoredProperties.Contains(p.Metadata.Name) &&
            !IgnoredProperties.GetOrAdd(info, i => i.IsDefined(typeof(AuditIgnoreAttribute), inherit: true)));

    private static bool IsSoftDelete(EntityEntry entry, AuditEngineOptions options)
    {
        if (options.SoftDeleteProperty == null) return false;
        var property = entry.Metadata.FindProperty(options.SoftDeleteProperty);
        if (property == null) return false;
        var value = entry.Property(property.Name);
        return value.IsModified && value.OriginalValue == null && value.CurrentValue != null;
    }

    internal static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        // Without the column's trailing zeros: 435, not 435.000000000000000000000000000000.
        decimal m => m.ToString("0.############################", CultureInfo.InvariantCulture),
        byte[] bytes => $"[{bytes.Length} bytes]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
