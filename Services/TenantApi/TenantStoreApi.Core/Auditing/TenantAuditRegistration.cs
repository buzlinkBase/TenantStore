using BuzlinkRepository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.AuditEngine;
using Onepunch.AuditEngine.AspNetCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.AuditEngine.MassTransit;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Auditing;

/// <summary>
/// TenantApi's audit trail: membership, role and permission changes ([Audited] entities) are
/// recorded in TenantContext's own AuditEvents table, in the same transaction as the change, and
/// forwarded through the EF outbox to hrms-api's combined Security > Audit view.
/// </summary>
public static class TenantAuditRegistration
{
    public const string SourceName = "tenant-api";

    /// <param name="configuration">"AuditEngine:Enabled" = false turns capture off, e.g. to deploy
    /// before the AuditEvents table is migrated.</param>
    public static IServiceCollection AddTenantAuditTrail(this IServiceCollection services, IConfiguration configuration) =>
        services.AddAuditEngine(o =>
        {
            o.Options.SourceName = SourceName;
            o.Options.Enabled = configuration.GetValue("AuditEngine:Enabled", true);
            o.UseEntityFrameworkCore<TenantContext>();
            o.UseAspNetCore(claims => claims.OrganizationResolver = TenantOrganization);
            o.ForwardToMassTransit(f => f.OrganizationHeaderName = "X-Tenant-ID");
        });

    // The company a change belongs to: the request's (or the consumed message's) tenant; null
    // falls back to the token's tenantId claim.
    private static string? TenantOrganization(IServiceProvider services) =>
        services.GetRequiredService<ITenantProvider>().TenantId is var tenantId && tenantId != Guid.Empty
            ? tenantId.ToString()
            : null;
}
