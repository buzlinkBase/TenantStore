using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Onepunch.AuditEngine.Pipeline;

namespace Onepunch.AuditEngine;

public sealed class AuditEngineOptions
{
    /// <summary>Recorded as <see cref="AuditEvent.Source"/>, e.g. "hrms-api".</summary>
    public string SourceName { get; set; } = "unknown";

    /// <summary>
    /// Off = nothing is captured or written (reads still work). Lets a host deploy the code before
    /// its audit table exists, then switch capture on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Property names whose values are masked in recorded changes, on any entity.</summary>
    public ISet<string> RedactedProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "RefreshToken", "TokenValue",
    };

    public string RedactionMask { get; set; } = "***";

    /// <summary>
    /// Properties never recorded as changes, on any entity -- bookkeeping columns every row has,
    /// which would otherwise appear in every update. Per-entity exclusions use [AuditIgnore].
    /// </summary>
    public ISet<string> IgnoredProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CreatedAt", "UpdatedAt", "DeletedAt",
    };

    /// <summary>
    /// A soft-deleted row is saved as an update that sets this property; it's recorded as a
    /// delete. Null when the host doesn't soft-delete.
    /// </summary>
    public string? SoftDeleteProperty { get; set; } = "DeletedAt";
}

/// <summary>
/// Composes the engine: pick exactly one storage adapter (e.g. <c>UseEntityFrameworkCore</c>),
/// plus the host's actor resolver, metadata enrichers and event sinks. Switching datastores
/// later means changing only the storage call here.
/// </summary>
public sealed class AuditEngineBuilder(IServiceCollection services, AuditEngineOptions options)
{
    public IServiceCollection Services { get; } = services;

    public AuditEngineOptions Options { get; } = options;

    internal bool HasStorage { get; private set; }

    /// <summary>
    /// For storage adapters: registers the store under <see cref="AuditEngineServiceKeys.StorageWriter"/>
    /// (keyed <see cref="IAuditWriter"/>), plus <see cref="IAuditChainHead"/>,
    /// <see cref="IAuditQueryService"/> and <see cref="IAuditReplicaWriter"/>.
    /// </summary>
    public AuditEngineBuilder UseStorage(Action<IServiceCollection> registerStorage)
    {
        if (HasStorage)
            throw new InvalidOperationException("The audit engine already has a storage adapter; configure only one.");
        registerStorage(Services);
        HasStorage = true;
        return this;
    }

    public AuditEngineBuilder UseActorResolver<TResolver>() where TResolver : class, IAuditActorResolver
    {
        Services.AddScoped<IAuditActorResolver, TResolver>();
        return this;
    }

    public AuditEngineBuilder AddMetadataEnricher<TEnricher>() where TEnricher : class, IAuditMetadataEnricher
    {
        Services.AddScoped<IAuditMetadataEnricher, TEnricher>();
        return this;
    }

    public AuditEngineBuilder AddSink<TSink>() where TSink : class, IAuditEventSink
    {
        Services.AddScoped<IAuditEventSink, TSink>();
        return this;
    }
}

public static class AuditEngineServiceKeys
{
    /// <summary>Key of the storage adapter's own <see cref="IAuditWriter"/> (the innermost stage).</summary>
    public const string StorageWriter = "Onepunch.AuditEngine.Storage";
}

public static class AuditEngineServiceCollectionExtensions
{
    public static IServiceCollection AddAuditEngine(this IServiceCollection services, Action<AuditEngineBuilder> configure)
    {
        var options = new AuditEngineOptions();
        var builder = new AuditEngineBuilder(services, options);
        configure(builder);

        if (!builder.HasStorage)
            throw new InvalidOperationException(
                "The audit engine needs a storage adapter, e.g. UseEntityFrameworkCore<TContext>().");

        services.AddSingleton(options);
        services.TryAddScoped<IAuditActorResolver>(_ => new FixedAuditActorResolver(AuditActor.System(options.SourceName)));
        services.TryAddSingleton<IAuditRedactor>(_ => new PropertyNameAuditRedactor(options.RedactedProperties, options.RedactionMask));

        // Pipeline, outermost first: enrich (+ redact) -> hash-chain -> store.
        services.AddScoped<IAuditWriter>(sp => options.Enabled
            ? new EnrichingAuditWriter(
                new HashChainingAuditWriter(
                    sp.GetRequiredKeyedService<IAuditWriter>(AuditEngineServiceKeys.StorageWriter),
                    sp.GetRequiredService<IAuditChainHead>()),
                sp.GetRequiredService<IAuditActorResolver>(),
                sp.GetServices<IAuditMetadataEnricher>(),
                sp.GetRequiredService<IAuditRedactor>(),
                options)
            : NullAuditWriter.Instance);

        return services;
    }
}
