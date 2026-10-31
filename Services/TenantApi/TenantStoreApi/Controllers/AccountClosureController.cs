using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record ClosureTokenBody
{
    public string? Token { get; set; }
}

/// <summary>
/// The two links in an account-closure email: "confirm" and "this wasn't me, cancel". Anonymous on purpose
/// (the owner may be signed out or on another device); each works only with its own long random one-time
/// token, which is the proof. A link that doesn't match anything is answered the same way whatever the
/// reason, so it tells a guesser nothing.
/// </summary>
[Route("api/v{version:apiVersion}/account-closure")]
[ApiVersion("1.0")]
[AllowAnonymous]
[ApiController]
public class AccountClosureLinksController : ControllerBase
{
    private readonly AccountClosureService _closures;

    public AccountClosureLinksController(AccountClosureService closures) => _closures = closures;

    [HttpPost("confirm")]
    public Task<IActionResult> Confirm([FromBody] ClosureTokenBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var done = await _closures.ConfirmAsync(body.Token, token);
            if (!done) return NotFound("This link isn't valid any more.");
            await _closures.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("cancel")]
    public Task<IActionResult> Cancel([FromBody] ClosureTokenBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var done = await _closures.CancelByTokenAsync(body.Token, token);
            if (!done) return NotFound("This link isn't valid any more.");
            await _closures.CommitChangesAsync(token);
            return Ok();
        });
}

/// <summary>The console's queue of owners who asked to close their account: ready for review, or still cooling off.</summary>
[Route("api/v{version:apiVersion}/platform/closure-requests")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformClosureRequestsController : ControllerBase
{
    private readonly AccountClosureService _closures;

    public PlatformClosureRequestsController(AccountClosureService closures) => _closures = closures;

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    [ProducesResponseType(typeof(IReadOnlyList<AccountClosureReviewView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await _closures.ListForReviewAsync(token));

    /// <summary>Locks the account as closed at the owner's request and schedules its data for purge.</summary>
    [HttpPost("{requestId:guid}/approve")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> Approve(Guid requestId, [FromBody] ApproveDeletionBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _closures.ApproveAsync(requestId, body.ConfirmEmail, PlatformActor.Operator(User), token);
            await _closures.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("{requestId:guid}/reject")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> Reject(Guid requestId, [FromBody] ReasonRequest body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _closures.RejectAsync(requestId, body.Reason, PlatformActor.Operator(User), token);
            await _closures.CommitChangesAsync(token);
            return Ok();
        });
}
