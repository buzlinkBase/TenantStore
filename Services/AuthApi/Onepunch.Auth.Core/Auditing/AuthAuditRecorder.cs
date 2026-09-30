using BuzlinkRepository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.AuditEngine;
using Onepunch.AuditEngine.AspNetCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.AuditEngine.MassTransit;
using Onepunch.Auth.Infrastructure;
using OnePunch.Auth.Domain.Entities;

namespace OnePunch.Auth.Core.Auditing;

/// <summary>
/// Sign-in and sign-out events. Written explicitly (not captured from entity changes): a failed
/// sign-in changes little or nothing, and a sign-in request is anonymous until it succeeds, so
/// the event names the account itself. Each event belongs to the account's default company --
/// the company its token is issued for -- which is also what gets it into that company's
/// Security > Audit view in hrms-api. An attempt for an unknown email belongs to no company and
/// stays in AuthApi's own trail.
/// Like every audit write, these commit with the caller's unit of work.
/// </summary>
public sealed class AuthAuditRecorder(IAuditWriter writer)
{
    public const string EntityType = "User";
    public const string PasswordMethod = "Password";
    public const string GoogleMethod = "Google";
    public const string AttemptedEmailKey = "AttemptedEmail";
    public const string MethodKey = "Method";

    public Task SignedInAsync(User user, string method, CancellationToken token) =>
        writer.WriteAsync(new AuditEvent
        {
            EventType = AuditEventTypes.Login,
            EntityType = EntityType,
            EntityId = user.Id.ToString(),
            OrganizationId = CompanyOf(user),
            Actor = ActorOf(user),
            Description = $"Signed in ({method})",
            Metadata = new Dictionary<string, string?> { [MethodKey] = method },
        }, token);

    public Task SignInFailedAsync(string attemptedEmail, User? user, string reason, string method, CancellationToken token) =>
        writer.WriteAsync(new AuditEvent
        {
            EventType = AuditEventTypes.Login,
            Outcome = AuditOutcome.Failure,
            EntityType = EntityType,
            EntityId = user?.Id.ToString(),
            OrganizationId = user == null ? null : CompanyOf(user),
            Actor = user == null ? new AuditActor(attemptedEmail, attemptedEmail, AuditActorTypes.Anonymous) : ActorOf(user),
            Description = $"Sign-in failed ({method}): {reason}",
            Metadata = new Dictionary<string, string?> { [MethodKey] = method, [AttemptedEmailKey] = attemptedEmail },
        }, token);

    public Task SignedOutAsync(User user, CancellationToken token) =>
        writer.WriteAsync(new AuditEvent
        {
            EventType = AuditEventTypes.Logout,
            EntityType = EntityType,
            EntityId = user.Id.ToString(),
            OrganizationId = CompanyOf(user),
            Actor = ActorOf(user),
            Description = "Signed out",
        }, token);

    public const string InvitationBatchSent = "INVITATION_BATCH_SENT";

    /// <summary>
    /// One event per batch invitation: who sent it, to whom (each email with the employee or
    /// client it's for), with which role. Belongs to the company invited into.
    /// </summary>
    public Task InvitationBatchSentAsync(
        User actor,
        Guid tenantId,
        Onepunch.Auth.Domain.DTOs.InviteEntityType entityType,
        string role,
        Guid? clientId,
        IReadOnlyCollection<Onepunch.Auth.Domain.DTOs.BatchInvitationRecipient> recipients,
        CancellationToken token) =>
        writer.WriteAsync(new AuditEvent
        {
            EventType = InvitationBatchSent,
            EntityType = "Invitation",
            OrganizationId = tenantId.ToString(),
            Actor = ActorOf(actor),
            Description = $"Invited {recipients.Count} {(entityType == Onepunch.Auth.Domain.DTOs.InviteEntityType.Client ? "client login(s)" : "employee(s)")} as {role}",
            Metadata = new Dictionary<string, string?>
            {
                ["EntityType"] = entityType.ToString(),
                ["Role"] = role,
                ["ClientId"] = clientId?.ToString(),
                ["Recipients"] = string.Join("; ", recipients.Select(r =>
                    entityType == Onepunch.Auth.Domain.DTOs.InviteEntityType.Client
                        ? $"{r.Email} (client {clientId})"
                        : $"{r.Email} (employee {r.EntityId})")),
            },
        }, token);

    private static AuditActor ActorOf(User user) =>
        AuditActor.User(user.Id.ToString(), user.Email ?? user.FullName ?? user.Id.ToString());

    private static string? CompanyOf(User user) =>
        user.DefaultTenantId is { } tenantId && tenantId != Guid.Empty ? tenantId.ToString() : null;
}

public static class AuthAuditRegistration
{
    public const string SourceName = "auth-api";

    /// <param name="configuration">"AuditEngine:Enabled" = false turns capture off, e.g. to deploy
    /// before the AuditEvents table is migrated.</param>
    public static IServiceCollection AddAuthAuditTrail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuthAuditRecorder>();
        return services.AddAuditEngine(o =>
        {
            o.Options.SourceName = SourceName;
            o.Options.Enabled = configuration.GetValue("AuditEngine:Enabled", true);
            o.UseEntityFrameworkCore<AuthContext>();
            o.UseAspNetCore(claims => claims.OrganizationResolver = TenantOrganization);
            o.ForwardToMassTransit(f => f.OrganizationHeaderName = "X-Tenant-ID");
        });
    }

    private static string? TenantOrganization(IServiceProvider services) =>
        services.GetService<ITenantProvider>()?.TenantId is { } tenantId && tenantId != Guid.Empty
            ? tenantId.ToString()
            : null;
}
