using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.AuditEngine.Pipeline;

namespace Onepunch.AuditEngine.Tests;

[Audited]
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BankAccountNo { get; set; }
    [AuditIgnore] public string? Notes { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

[Audited(EntityType = "Invoice")]
public class InvoiceRow
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Created by a person, then updated by a machine -- only the creation is an action.</summary>
[Audited(Capture = AuditCapture.Create | AuditCapture.Delete)]
public class QueuedCommand
{
    public Guid Id { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

/// <summary>Not marked -- must never produce events.</summary>
public class CacheRow
{
    public Guid Id { get; set; }
    public string Value { get; set; } = string.Empty;
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<InvoiceRow> Invoices => Set<InvoiceRow>();
    public DbSet<CacheRow> Cache => Set<CacheRow>();
    public DbSet<QueuedCommand> Commands => Set<QueuedCommand>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyAuditEngineModel();
}

public sealed class RecordingSink : IAuditEventSink
{
    public List<AuditEvent> Seen { get; } = [];

    public Task OnStagedAsync(IReadOnlyList<AuditEvent> events, CancellationToken token = default)
    {
        Seen.AddRange(events);
        return Task.CompletedTask;
    }
}

/// <summary>A host wired up exactly like production: DI, AddAuditEngine, AddDbContext.</summary>
public sealed class AuditHost : IDisposable
{
    public static readonly AuditActor Alice = AuditActor.User("user-1", "Alice");
    public const string Org = "org-1";

    private readonly ServiceProvider _root;

    public AuditHost(Action<AuditEngineBuilder>? configure = null, bool enabled = true)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<RecordingSink>();
        services.AddAuditEngine(o =>
        {
            o.Options.SourceName = "test-api";
            o.Options.Enabled = enabled;
            o.Options.RedactedProperties.Add("BankAccountNo");
            o.UseEntityFrameworkCore<TestDbContext>();
            o.Services.AddScoped<IAuditActorResolver>(_ => new FixedAuditActorResolver(Alice, Org));
            o.Services.AddScoped<IAuditEventSink>(sp => sp.GetRequiredService<RecordingSink>());
            configure?.Invoke(o);
        });
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(databaseName));
        _root = services.BuildServiceProvider();
    }

    public RecordingSink Sink => _root.GetRequiredService<RecordingSink>();

    public IServiceScope Scope() => _root.CreateScope();

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Scope();
        return await action(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> action) =>
        InScopeAsync<bool>(async sp => { await action(sp); return true; });

    public Task<List<AuditEventRecord>> RecordsAsync() =>
        InScopeAsync(sp => sp.GetRequiredService<TestDbContext>().AuditEvents.AsNoTracking()
            .OrderBy(x => x.OccurredAtUtc).ToListAsync());

    public void Dispose() => _root.Dispose();
}
