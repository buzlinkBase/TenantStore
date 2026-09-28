using Microsoft.EntityFrameworkCore;

namespace Onepunch.AuditEngine.EntityFrameworkCore;

/// <summary>
/// The storage stage of the write pipeline for EF Core: stages events on the host's own context
/// (so they commit with the host's change), serves the chain head, and stores replicas.
/// </summary>
public sealed class EfCoreAuditStore<TContext>(TContext context, AuditEngineOptions options)
    : IAuditWriter, IAuditChainHead, IAuditReplicaWriter
    where TContext : DbContext
{
    private DbSet<AuditEventRecord> Records => context.Set<AuditEventRecord>();

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken token = default)
    {
        Records.Add(AuditEventRecordMapper.ToRecord(auditEvent));
        return Task.CompletedTask;
    }

    // Chains are per organization AND per source: a host keeping replicas of other services'
    // events must not link its own events onto theirs.
    public Task<string?> GetHeadHashAsync(string? organizationId, CancellationToken token = default) =>
        Records.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Source == options.SourceName)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => x.Hash)
            .FirstOrDefaultAsync(token);

    public async Task WriteReplicaAsync(AuditEvent auditEvent, CancellationToken token = default)
    {
        if (Records.Local.Any(x => x.Id == auditEvent.Id)) return;
        if (await Records.AsNoTracking().AnyAsync(x => x.Id == auditEvent.Id, token)) return;
        Records.Add(AuditEventRecordMapper.ToRecord(auditEvent));
    }
}

public sealed class EfCoreAuditQueryService<TContext>(TContext context) : IAuditQueryService
    where TContext : DbContext
{
    public const int MaxPageSize = 200;

    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken token = default)
    {
        var records = context.Set<AuditEventRecord>().AsNoTracking();

        if (query.OrganizationId != null) records = records.Where(x => x.OrganizationId == query.OrganizationId);
        if (query.ActorId != null) records = records.Where(x => x.ActorId == query.ActorId);
        if (query.EntityType != null) records = records.Where(x => x.EntityType == query.EntityType);
        if (query.EntityId != null) records = records.Where(x => x.EntityId == query.EntityId);
        if (query.EventType != null) records = records.Where(x => x.EventType == query.EventType);
        if (query.Outcome is { } outcome)
        {
            var value = outcome.ToString();
            records = records.Where(x => x.Outcome == value);
        }
        if (query.FromUtc is { } from) records = records.Where(x => x.OccurredAtUtc >= from);
        if (query.ToUtc is { } to) records = records.Where(x => x.OccurredAtUtc < to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            records = records.Where(x =>
                (x.Description != null && x.Description.Contains(term)) ||
                (x.ActorDisplayName != null && x.ActorDisplayName.Contains(term)) ||
                (x.EntityType != null && x.EntityType.Contains(term)) ||
                (x.EntityId != null && x.EntityId.Contains(term)));
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await records.CountAsync(token);
        var rows = await records
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(token);

        return new AuditPage(rows.Select(AuditEventRecordMapper.ToEvent).ToList(), total, page, pageSize);
    }

    public async Task<AuditEvent?> GetAsync(Guid id, string? organizationId, CancellationToken token = default)
    {
        var record = await context.Set<AuditEventRecord>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && (organizationId == null || x.OrganizationId == organizationId), token);
        return record == null ? null : AuditEventRecordMapper.ToEvent(record);
    }
}
