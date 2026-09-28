using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Onepunch.AuditEngine.EntityFrameworkCore;

/// <summary>
/// Row shape of an <see cref="AuditEvent"/> in the host's database. Changes and metadata are
/// stored as JSON text so adding a field to an audited entity never needs an audit-table
/// migration. Not sealed: hosts using lazy-loading proxies require unsealed entity types.
/// </summary>
public class AuditEventRecord
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OrganizationId { get; set; }
    public string? ActorId { get; set; }
    public string? ActorDisplayName { get; set; }
    public string? ActorType { get; set; }
    public string? Description { get; set; }
    public string? Changes { get; set; }
    public string? Metadata { get; set; }
    public string Outcome { get; set; } = nameof(AuditOutcome.Success);
    public string? Source { get; set; }
    public string? Hash { get; set; }
    public string? PreviousEventHash { get; set; }
}

public static class AuditModelBuilderExtensions
{
    public const string DefaultTableName = "AuditEvents";

    /// <summary>Adds the audit table to the host's model. Call from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder ApplyAuditEngineModel(this ModelBuilder modelBuilder, string tableName = DefaultTableName)
    {
        modelBuilder.Entity<AuditEventRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            b.Property(x => x.EntityType).HasMaxLength(128);
            b.Property(x => x.EntityId).HasMaxLength(128);
            b.Property(x => x.OrganizationId).HasMaxLength(64);
            b.Property(x => x.ActorId).HasMaxLength(128);
            b.Property(x => x.ActorDisplayName).HasMaxLength(256);
            b.Property(x => x.ActorType).HasMaxLength(32);
            b.Property(x => x.Description).HasMaxLength(1024);
            b.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
            b.Property(x => x.Source).HasMaxLength(64);
            b.Property(x => x.Hash).HasMaxLength(64);
            b.Property(x => x.PreviousEventHash).HasMaxLength(64);

            b.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
            b.HasIndex(x => new { x.OrganizationId, x.ActorId, x.OccurredAtUtc });
            b.HasIndex(x => new { x.OrganizationId, x.EntityType, x.EntityId });
            b.HasIndex(x => new { x.OrganizationId, x.Source, x.OccurredAtUtc });
        });
        return modelBuilder;
    }
}

internal static class AuditEventRecordMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AuditEventRecord ToRecord(AuditEvent e) => new()
    {
        Id = e.Id,
        OccurredAtUtc = e.OccurredAtUtc,
        EventType = e.EventType,
        EntityType = e.EntityType,
        EntityId = e.EntityId,
        OrganizationId = e.OrganizationId,
        ActorId = e.Actor?.Id,
        ActorDisplayName = e.Actor?.DisplayName,
        ActorType = e.Actor?.Type,
        Description = e.Description,
        Changes = e.Changes.Count == 0 ? null : JsonSerializer.Serialize(e.Changes, Json),
        Metadata = e.Metadata.Count == 0 ? null : JsonSerializer.Serialize(e.Metadata, Json),
        Outcome = e.Outcome.ToString(),
        Source = e.Source,
        Hash = e.Hash,
        PreviousEventHash = e.PreviousEventHash,
    };

    public static AuditEvent ToEvent(AuditEventRecord r) => new()
    {
        Id = r.Id,
        OccurredAtUtc = DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc),
        EventType = r.EventType,
        EntityType = r.EntityType,
        EntityId = r.EntityId,
        OrganizationId = r.OrganizationId,
        Actor = r.ActorId == null ? null : new AuditActor(r.ActorId, r.ActorDisplayName, r.ActorType ?? AuditActorTypes.User),
        Description = r.Description,
        Changes = r.Changes == null ? [] : JsonSerializer.Deserialize<List<AuditChange>>(r.Changes, Json) ?? [],
        Metadata = r.Metadata == null
            ? new Dictionary<string, string?>()
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(r.Metadata, Json) ?? new Dictionary<string, string?>(),
        Outcome = Enum.TryParse<AuditOutcome>(r.Outcome, out var outcome) ? outcome : AuditOutcome.Success,
        Source = r.Source,
        Hash = r.Hash,
        PreviousEventHash = r.PreviousEventHash,
    };
}
