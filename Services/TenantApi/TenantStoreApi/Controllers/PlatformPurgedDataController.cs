using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record PurgedAuditRequest
{
    /// <summary>Required: who asked for the trail, or the legal duty it serves.</summary>
    public string? Reason { get; set; }
    public string? Search { get; set; }
    public string? Action { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public record DestroyArchivesRequestBody
{
    public string? Reason { get; set; }
    /// <summary>The account owner's email, typed by the operator to confirm.</summary>
    public string? ConfirmEmail { get; set; }
}

/// <summary>
/// The console's "Purged data": which accounts and companies were purged (their data archived and removed
/// from the live databases), their retained audit trail, a download link for each archive, and destroying
/// the archives before their retention date. Reading the trail or an archive needs a reason and is
/// recorded; an early destroy needs two different operators.
/// </summary>
[Route("api/v{version:apiVersion}/platform/purged-data")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformPurgedDataController : ControllerBase
{
    private readonly PurgedDataService _purged;
    private readonly ArchiveDestroyRequestService _destroy;

    public PlatformPurgedDataController(PurgedDataService purged, ArchiveDestroyRequestService destroy)
    {
        _purged = purged;
        _destroy = destroy;
    }

    /// <summary>Purged accounts, found by owner email or company name. Filter: purged, destroyed or hold.</summary>
    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.PurgedDataView)]
    [ProducesResponseType(typeof(PurgedAccountPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PurgedDataQuery query, CancellationToken token) =>
        Ok(await _purged.ListAsync(query, token));

    [HttpPost("{ownerUserId:guid}/companies/{tenantId:guid}/audit")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    [ProducesResponseType(typeof(PurgedAuditPage), StatusCodes.Status200OK)]
    public Task<IActionResult> Audit(Guid ownerUserId, Guid tenantId, [FromBody] PurgedAuditRequest body, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _purged.GetAuditAsync(
            ownerUserId, tenantId, new PurgedAuditQuery(body.Search, body.Action, body.From, body.To, body.Page, body.PageSize),
            body.Reason, PlatformActor.Operator(User), token)));

    [HttpPost("{ownerUserId:guid}/companies/{tenantId:guid}/archive-link")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    [ProducesResponseType(typeof(PurgedArchiveLink), StatusCodes.Status200OK)]
    public Task<IActionResult> ArchiveLink(Guid ownerUserId, Guid tenantId, [FromBody] ReasonRequest body, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _purged.GetArchiveLinkAsync(
            ownerUserId, tenantId, body.Reason, PlatformActor.Operator(User), token)));

    // ── Destroying archives early ────────────────────────────────────────────

    /// <summary>The account's latest request to destroy its archives (any outcome); 204 when it never had one.</summary>
    [HttpGet("{ownerUserId:guid}/destroy-request")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataView)]
    [ProducesResponseType(typeof(ArchiveDestroyRequestView), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDestroyRequest(Guid ownerUserId, CancellationToken token)
    {
        var request = await _destroy.GetLatestAsync(ownerUserId, token);
        return request is null ? NoContent() : Ok(request);
    }

    [HttpGet("destroy-requests")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    [ProducesResponseType(typeof(IReadOnlyList<ArchiveDestroyRequestView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPendingDestroyRequests(CancellationToken token) =>
        Ok(await _destroy.ListPendingAsync(token));

    /// <summary>Asks for the account's archives to be destroyed now. Nothing happens until a second operator approves.</summary>
    [HttpPost("{ownerUserId:guid}/destroy-requests")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    [ProducesResponseType(typeof(ArchiveDestroyRequestView), StatusCodes.Status200OK)]
    public Task<IActionResult> RequestDestroy(Guid ownerUserId, [FromBody] DestroyArchivesRequestBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var request = await _destroy.RequestAsync(ownerUserId, body.Reason, body.ConfirmEmail, PlatformActor.Operator(User), token);
            await _destroy.CommitChangesAsync(token);
            return Ok(request);
        });

    [HttpPost("{ownerUserId:guid}/destroy-requests/{requestId:guid}/withdraw")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    public Task<IActionResult> WithdrawDestroy(Guid ownerUserId, Guid requestId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _destroy.WithdrawAsync(ownerUserId, requestId, PlatformActor.Operator(User), token);
            await _destroy.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Approves it and asks for the archives to be destroyed; the account shows "archive destroyed" once hrms-api has done it.</summary>
    [HttpPost("destroy-requests/{requestId:guid}/approve")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    public Task<IActionResult> ApproveDestroy(Guid requestId, [FromBody] ApproveDeletionBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _destroy.ApproveAsync(requestId, body.ConfirmEmail, PlatformActor.Operator(User), token);
            await _destroy.CommitChangesAsync(token);
            return Accepted();
        });

    [HttpPost("destroy-requests/{requestId:guid}/reject")]
    [PlatformAuthorize(PlatformPermissions.PurgedDataManage)]
    public Task<IActionResult> RejectDestroy(Guid requestId, [FromBody] ReasonRequest body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _destroy.RejectAsync(requestId, body.Reason, PlatformActor.Operator(User), token);
            await _destroy.CommitChangesAsync(token);
            return Ok();
        });
}
