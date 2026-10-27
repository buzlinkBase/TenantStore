using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Subscriptions;
using OnePunch.Auth.Domain.Entities;
using Onepunch.Auth.Core.Services;
using BuzlinkRepository;

namespace Onepunch.Auth.Core.Platform;

/// <summary>What the console gets back after asking for a company: the new company and its owner.</summary>
public record PlatformCompanyRequestResult(Guid TenantId, string TenantName, Guid OwnerUserId, string? OwnerEmail, string Status);

/// <summary>
/// Setting up a company for an account owner from the Platform Admin Console. Runs the same path as
/// the owner creating one themselves (<see cref="WorkspaceService.Create"/>): the plan's company limit
/// is checked, then <see cref="TenantCreationRequested"/> is published and Tenant Service makes the
/// owner its Owner. The difference is who is acting: the request is for <c>ownerUserId</c>, nothing is
/// signed in as that person, and the operator who asked is logged.
/// </summary>
public class PlatformCompanyService
{
    private readonly UserManager<User> _users;
    private readonly TenantRequestService _requests;
    private readonly MembershipGrpcClient _membership;
    private readonly IPublishEndpoint _publisher;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<PlatformCompanyService> _logger;

    public PlatformCompanyService(
        UserManager<User> users,
        TenantRequestService requests,
        MembershipGrpcClient membership,
        IPublishEndpoint publisher,
        ITenantProvider tenantProvider,
        ILogger<PlatformCompanyService> logger)
    {
        _users = users;
        _requests = requests;
        _membership = membership;
        _publisher = publisher;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    public async Task<PlatformCompanyRequestResult> CreateForOwnerAsync(
        Guid ownerUserId, CreateWorkspaceRequest payload, string actor, CancellationToken ct)
    {
        var tenantName = payload.TenantName?.Trim();
        if (string.IsNullOrWhiteSpace(tenantName)) throw new GuardException("Enter the company's name.");

        var owner = await _users.FindByIdAsync(ownerUserId.ToString())
            ?? throw new GuardException("That account doesn't exist.");

        // One company at a time per person, same as the owner's own flow: a company still being
        // set up has to finish (or expire) before another is started.
        if (await _requests.FindPendingByUser(owner.Id) != null)
            throw new GuardException("A company is still being set up for this account. Try again once it has finished.");

        // The account's plan decides how many companies it may own, whoever asks.
        await _membership.EnsurePlanAllowsAsync(EntitlementKeys.CompaniesMax, owner.Id, tenantId: null);

        var request = BuildRequest(owner, tenantName, payload);
        _tenantProvider.SetTenantId(request.TenantId);
        await _requests.Store(new TenantCreationRequestStatus
        {
            TenantId = request.TenantId,
            Status = TenantCreationStatus.Provisioning,
            UserId = owner.Id,
            TenantName = request.TenantName,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        });
        await _publisher.Publish(request, ct);
        await _requests.CommitChangesAsync(ct);

        _logger.LogWarning(
            "Platform admin {Actor} requested company {TenantId} ({TenantName}) for account owner {OwnerUserId} ({OwnerEmail})",
            actor, request.TenantId, request.TenantName, owner.Id, owner.Email);

        return new PlatformCompanyRequestResult(
            request.TenantId, request.TenantName, owner.Id, owner.Email, TenantCreationStatus.Provisioning.ToString());
    }

    /// <summary>The event Tenant Service acts on -- identical to what the owner's own sign-up sends.</summary>
    public static TenantCreationRequested BuildRequest(User owner, string tenantName, CreateWorkspaceRequest payload) => new()
    {
        TenantId = Guid.NewGuid(),
        Email = owner.Email ?? "",
        FullName = owner.FullName ?? "Account Owner",
        UserId = owner.Id,
        TenantName = tenantName,
        Profile = CompanyProfile.Normalize(WorkspaceService.ToProfile(payload)),
    };
}
