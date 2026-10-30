using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Onepunch.AuditEngine.EntityFrameworkCore;

/// <summary>
/// Records creates/updates/deletes of <see cref="AuditedAttribute"/> entities as part of the
/// host's own save: the events are staged on the same context before EF writes anything, so the
/// trail and the change it describes commit (or roll back) together. Then every
/// <see cref="IAuditEventSink"/> sees all events staged in this save -- captured here or written
/// explicitly through <see cref="IAuditWriter"/> earlier in the unit of work.
/// Services are resolved per save, not at construction: the writer depends on the very context
/// this interceptor is attached to.
/// </summary>
public sealed class AuditSaveChangesInterceptor(IServiceProvider services) : SaveChangesInterceptor
{
    private bool _capturing;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await CaptureAsync(eventData.Context, cancellationToken);
        return result;
    }

    private async Task CaptureAsync(DbContext? context, CancellationToken token)
    {
        if (context == null || _capturing) return;
        var options = services.GetService<AuditEngineOptions>();
        if (options is not { Enabled: true }) return;
        if (services.GetService<IAuditActorResolver>()?.IsAuditSuppressed() == true) return;

        _capturing = true;
        try
        {
            // Interceptors run before SaveChanges' own DetectChanges.
            context.ChangeTracker.DetectChanges();

            var captured = AuditEntryCapture.Capture(context.ChangeTracker.Entries().ToList(), options);
            if (captured.Count > 0)
            {
                var writer = services.GetRequiredService<IAuditWriter>();
                foreach (var auditEvent in captured)
                    await writer.WriteAsync(auditEvent, token);
            }

            var sinks = services.GetServices<IAuditEventSink>().ToList();
            if (sinks.Count == 0) return;

            var staged = context.ChangeTracker.Entries<AuditEventRecord>()
                .Where(x => x.State == EntityState.Added)
                .Select(x => AuditEventRecordMapper.ToEvent(x.Entity))
                .ToList();
            if (staged.Count == 0) return;

            foreach (var sink in sinks)
                await sink.OnStagedAsync(staged, token);
        }
        finally
        {
            _capturing = false;
        }
    }
}
