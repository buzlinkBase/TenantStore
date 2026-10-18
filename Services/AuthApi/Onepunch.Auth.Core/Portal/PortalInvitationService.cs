using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.Security.Cryptography;

namespace Onepunch.Auth.Core.Portal;

/// <summary>
/// Invitations for client-portal users: the one place a person is invited to a client. Who may invite
/// whom is Tenant Service's call (it holds each client's roles), so this asks it before sending
/// anything and again when a person accepts; this service owns the link, the account and the email.
///
/// The link carries a random token of which only the hash is stored, so a copy of the database can't
/// be used to accept someone's invitation. These invitations are a category of their own
/// (<see cref="InvitationCategory.ClientUser"/>): the HRIS accept, preview and "my invitations" paths
/// never see them, and accepting one never creates an HRIS membership.
/// </summary>
public class PortalInvitationService : BaseService<Invitation>
{
    private const string Invalid = "This invitation link is invalid.";

    private readonly UserManager<User> _manager;
    private readonly MembershipGrpcClient _membership;
    private readonly IPublishEndpoint _publisher;
    private readonly PortalAuthSettings _settings;
    private readonly TimeProvider _time;

    public PortalInvitationService(
        IUnitOfWorkService uow,
        UserManager<User> manager,
        MembershipGrpcClient membership,
        IPublishEndpoint publisher,
        IOptions<PortalAuthSettings> settings,
        TimeProvider time) : base(uow)
    {
        _manager = manager;
        _membership = membership;
        _publisher = publisher;
        _settings = settings.Value;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>Sends an invitation. Any still-valid one for the same email and client is superseded.</summary>
    public async Task<PortalInvitationView> InviteAsync(PortalInviteRequest request, CancellationToken ct)
    {
        var email = InvitationPolicy.Normalize(request.Email ?? string.Empty);
        if (!InvitationPolicy.IsValidEmail(email)) throw new GuardException("A valid email is required.");
        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        if (roleIds.Count == 0) throw new GuardException("Choose at least one role.");

        var check = await _membership.CheckClientInviteAsync(request.InviterUserId, request.ClientEntityId, email, roleIds);
        if (!check.Allowed) throw new GuardException(check.Error ?? "You can't invite that person.");

        foreach (var prior in await PendingQuery(tracked: true)
                     .Where(x => x.ClientEntityId == request.ClientEntityId && x.Email == email)
                     .ToListAsync(ct))
        {
            prior.Expiry = Now;
        }

        var invitation = new Invitation
        {
            Category = InvitationCategory.ClientUser,
            UserId = request.InviterUserId,
            TenantId = check.TenantId,
            TenantName = check.TenantName,
            ClientEntityId = request.ClientEntityId,
            ClientName = check.ClientName,
            ClientRoleIds = roleIds,
            Email = email,
            Name = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim(),
            Roles = new(), // Staff roles by name; a client invitation carries role ids instead.
            Status = InvitationStatus.Pending,
        };
        var token = IssueToken(invitation);
        Repository.Add(invitation);
        await PublishEmailAsync(invitation, token, ct);
        await CommitChangesAsync(ct);
        return ToView(invitation);
    }

    /// <summary>The invitations of a client still waiting to be accepted (read-only; the portal has already authorized the viewer).</summary>
    public async Task<List<PortalInvitationView>> ListPendingAsync(Guid clientEntityId, CancellationToken ct)
    {
        var now = Now;
        var pending = await PendingQuery(tracked: false)
            .Where(x => x.ClientEntityId == clientEntityId && x.Expiry > now)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        return pending.Select(ToView).ToList();
    }

    /// <summary>A fresh link (the old one stops working) for an invitation that is still pending or has lapsed.</summary>
    public async Task<PortalInvitationView> ResendAsync(Guid invitationId, Guid actingUserId, CancellationToken ct)
    {
        var invitation = await FindPendingAsync(invitationId, ct);
        await EnsureMayActAsync(invitation, actingUserId);

        var token = IssueToken(invitation);
        await PublishEmailAsync(invitation, token, ct);
        await CommitChangesAsync(ct);
        return ToView(invitation);
    }

    /// <summary>Withdraws an invitation: its link stops working at once.</summary>
    public async Task RevokeAsync(Guid invitationId, Guid actingUserId, CancellationToken ct)
    {
        var invitation = await FindPendingAsync(invitationId, ct);
        await EnsureMayActAsync(invitation, actingUserId);

        invitation.Expiry = Now;
        await CommitChangesAsync(ct);
    }

    /// <summary>Lets the accept page say who invited them, to which client, before asking for anything. Null for an unknown link.</summary>
    public async Task<PortalInvitationPreview?> PreviewAsync(string? token, CancellationToken ct)
    {
        var invitation = await FindByTokenAsync(token, tracked: false, ct);
        if (invitation == null) return null;

        return new PortalInvitationPreview(
            invitation.Email,
            invitation.Name,
            invitation.ClientName ?? string.Empty,
            invitation.TenantName ?? string.Empty,
            invitation.Status == InvitationStatus.Pending && invitation.Expiry > Now,
            await _manager.FindByEmailAsync(invitation.Email) != null,
            invitation.Expiry);
    }

    /// <summary>
    /// Accepts an invitation: makes sure the person has a login (a new one needs a name and password; an
    /// existing account is left exactly as it is -- holding the emailed link is proof enough of the address),
    /// then has Tenant Service grant them the invited roles at the client. Nothing is marked accepted unless
    /// that grant succeeded, so a failed attempt can simply be retried.
    /// </summary>
    public async Task<PortalAcceptInvitationResult> AcceptAsync(PortalAcceptInvitationRequest request, CancellationToken ct)
    {
        var invitation = await FindByTokenAsync(request.Token, tracked: true, ct)
            ?? throw new GuardException(Invalid, "INVITATION_INVALID");
        if (invitation.Status == InvitationStatus.Accepted)
            throw new GuardException("This invitation has already been used.", "INVITATION_ALREADY_USED");
        if (invitation.Expiry <= Now)
            throw new GuardException("This invitation has expired. Ask whoever invited you to send a new one.", "INVITATION_EXPIRED");

        var existing = await _manager.FindByEmailAsync(invitation.Email);
        var accountId = existing?.Id ?? Guid.Empty;
        var fullName = existing?.FullName;
        var created = existing == null;
        if (created)
        {
            fullName = string.IsNullOrWhiteSpace(request.FullName) ? invitation.Name : request.FullName;
            if (string.IsNullOrWhiteSpace(fullName)) throw new GuardException("A name is required.");
            if (string.IsNullOrEmpty(request.Password)) throw new GuardException("A password is required.");

            var user = PortalAuthService.NewPortalUser(invitation.Email, fullName, invitation.Id.ToString(), Now);
            var result = await _manager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                throw new GuardException(string.Join(" ", result.Errors.Select(e => e.Description)));
            accountId = user.Id;
        }

        var (ok, error) = await _membership.GrantClientAccessAsync(
            accountId, invitation.ClientEntityId!.Value, invitation.Email, fullName, invitation.ClientRoleIds);
        if (!ok) throw new GuardException(error ?? "Access couldn't be granted. Please try again.");

        invitation.Status = InvitationStatus.Accepted;
        await CommitChangesAsync(ct);
        return new PortalAcceptInvitationResult(accountId, invitation.ClientEntityId.Value, created);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private IQueryable<Invitation> PendingQuery(bool tracked) =>
        GetQueryable(x => x.Category == InvitationCategory.ClientUser
                          && x.ClientEntityId != null
                          && x.Status == InvitationStatus.Pending, !tracked);

    private async Task<Invitation?> FindByTokenAsync(string? token, bool tracked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = PortalAuthService.HashToken(token);
        return await GetQueryable(x => x.Category == InvitationCategory.ClientUser && x.Token == hash, !tracked)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Invitation> FindPendingAsync(Guid invitationId, CancellationToken ct) =>
        await PendingQuery(tracked: true).FirstOrDefaultAsync(x => x.Id == invitationId, ct)
        ?? throw new GuardException("Invitation not found.");

    // Resending or withdrawing needs the same standing as sending it, so the same question is asked again.
    private async Task EnsureMayActAsync(Invitation invitation, Guid actingUserId)
    {
        var check = await _membership.CheckClientInviteAsync(
            actingUserId, invitation.ClientEntityId!.Value, invitation.Email, invitation.ClientRoleIds);
        if (!check.Allowed) throw new GuardException(check.Error ?? "You can't change that invitation.");
    }

    // Sets a new random token (storing only its hash) and a fresh expiry; returns the token to put in the link.
    private string IssueToken(Invitation invitation)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        invitation.Token = PortalAuthService.HashToken(token);
        invitation.Expiry = Now.Add(InvitationPolicy.LinkLifetime);
        return token;
    }

    private async Task PublishEmailAsync(Invitation invitation, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.PortalUrl))
            throw new GuardException("The client portal's address isn't configured, so invitations can't be sent.");

        await _publisher.Publish(new UserInvitionNotificationPayload
        {
            Email = invitation.Email,
            Name = invitation.Name ?? invitation.Email,
            Organization = invitation.ClientName ?? invitation.TenantName ?? string.Empty,
            Expiry = invitation.Expiry,
            InviteLink = $"{_settings.PortalUrl.TrimEnd('/')}/accept-invite?token={token}",
        }, ct);
    }

    private static PortalInvitationView ToView(Invitation x) =>
        new(x.Id, x.ClientEntityId!.Value, x.Email, x.Name, x.ClientRoleIds.ToList(), x.Expiry, x.CreatedAt);
}
