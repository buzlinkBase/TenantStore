using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Web.Authorization;
using TenantStoreApi.Core.Services.Legal;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record AcceptTermsRequest
{
    public int Version { get; set; }
}

/// <summary>
/// The Terms of Service as customers see them: the published text (anonymous, for the sign-up page
/// and the public Terms page), and the caller's company accepting it. A company accepts through an
/// owner or admin; an operator signed in as a user can't accept for them.
/// </summary>
[Route("api/v{version:apiVersion}/legal/terms")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class LegalTermsController : ControllerBase
{
    private readonly LegalDocumentService _documents;
    private readonly TermsAcceptanceService _acceptances;
    private readonly UserMembershipService _memberships;
    private readonly ITenantProvider _tenantProvider;

    public LegalTermsController(
        LegalDocumentService documents,
        TermsAcceptanceService acceptances,
        UserMembershipService memberships,
        ITenantProvider tenantProvider)
    {
        _documents = documents;
        _acceptances = acceptances;
        _memberships = memberships;
        _tenantProvider = tenantProvider;
    }

    [HttpGet("current")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublishedLegalDocument), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Current(CancellationToken token) =>
        // Not live: as if nothing were published (the HRIS hides the Terms; hrms-api skips the DOLE confirmation).
        _documents.IsLive && await _documents.GetPublishedAsync(LegalDocumentTypes.Terms, null, token) is { } current ? Ok(current) : NotFound();

    [HttpGet("{termsVersion:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublishedLegalDocument), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ByVersion(int termsVersion, CancellationToken token) =>
        _documents.IsLive && await _documents.GetPublishedAsync(LegalDocumentTypes.Terms, termsVersion, token) is { } found ? Ok(found) : NotFound();

    [HttpGet("status")]
    [ProducesResponseType(typeof(TermsStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        return Ok(await _acceptances.GetStatusAsync(tenantId, token));
    }

    [HttpPost("accept")]
    [ForbidWhenImpersonating]
    [ProducesResponseType(typeof(TermsStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Accept([FromBody] AcceptTermsRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var tenantId = _tenantProvider.TenantId;
            if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

            var userId = User.GetRequiredUserId();
            var member = await _memberships.GetMemberAsync(userId, tenantId, token);
            var mayAccept = member != null
                && string.Equals(member.Status, "Active", StringComparison.OrdinalIgnoreCase)
                && (member.HasRole(TenantRoles.Owner) || member.HasRole(TenantRoles.Admin));
            if (!mayAccept) return Forbid();

            var acceptor = new TermsAcceptor(
                userId,
                member!.InvitedEmail ?? User.FindFirst("email")?.Value,
                member.FullName,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());
            await _acceptances.AcceptAsync(tenantId, request.Version, acceptor, token);
            await _acceptances.CommitChangesAsync(token);
            return Ok(await _acceptances.GetStatusAsync(tenantId, token));
        });
}

/// <summary>
/// Writing and publishing the Terms from the console: one draft at a time, a preview with the live
/// settings filled in, and publishing as the next version. Who accepted which version is listed too.
/// </summary>
[Route("api/v{version:apiVersion}/platform/legal")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformLegalController : ControllerBase
{
    private const string DocType = LegalDocumentTypes.Terms;

    private readonly LegalDocumentService _documents;
    private readonly TermsAcceptanceService _acceptances;

    public PlatformLegalController(LegalDocumentService documents, TermsAcceptanceService acceptances)
    {
        _documents = documents;
        _acceptances = acceptances;
    }

    [HttpGet("terms/versions")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(List<LegalDocumentView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Versions(CancellationToken token) => Ok(await _documents.ListAsync(DocType, token));

    [HttpGet("terms/draft")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(LegalDocumentView), StatusCodes.Status200OK)]
    public async Task<IActionResult> Draft(CancellationToken token) => Ok(await _documents.GetDraftAsync(DocType, token));

    [HttpPut("terms/draft")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(LegalDocumentView), StatusCodes.Status200OK)]
    public Task<IActionResult> SaveDraft([FromBody] SaveLegalDraft request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var view = await _documents.SaveDraftAsync(DocType, request, PlatformActor.From(User), token);
            await _documents.CommitChangesAsync(token);
            return Ok(view);
        });

    [HttpDelete("terms/draft")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    public Task<IActionResult> DiscardDraft(CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _documents.DiscardDraftAsync(DocType, token);
            await _documents.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("terms/preview")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(LegalPreview), StatusCodes.Status200OK)]
    public IActionResult Preview([FromBody] SaveLegalDraft request) => Ok(_documents.Preview(request.BodyMarkdown));

    [HttpPost("terms/publish")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(LegalDocumentView), StatusCodes.Status200OK)]
    public Task<IActionResult> Publish(CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var view = await _documents.PublishAsync(DocType, PlatformActor.From(User), token);
            await _documents.CommitChangesAsync(token);
            return Ok(view);
        });

    [HttpGet("tokens")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(IReadOnlyList<LegalTokenView>), StatusCodes.Status200OK)]
    public IActionResult Tokens() => Ok(_documents.Tokens());

    [HttpGet("terms/acceptances")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(TermsAcceptancePage), StatusCodes.Status200OK)]
    public async Task<IActionResult> Acceptances([FromQuery] TermsAcceptanceQuery query, CancellationToken token) =>
        Ok(await _acceptances.ListAsync(query, token));
}
