using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record ReasonRequest
{
    public string? Reason { get; set; }
}

public record VoidPaymentRequest
{
    public string? Reason { get; set; }
    /// <summary>Refunded, Cancelled or EntryError: decides whether the owner still sees the payment.</summary>
    public VoidKind Kind { get; set; } = VoidKind.EntryError;
    /// <summary>Also put the plan back on what the previous live payment was for.</summary>
    public bool RevertPlan { get; set; }
}

public record RefundPaymentRequest
{
    public decimal Amount { get; set; }
    public GatewayRefundReason Reason { get; set; } = GatewayRefundReason.RequestedByCustomer;
    public string? Notes { get; set; }
}

public record PublicOfferingRequest
{
    public bool Show { get; set; }
    public string? Reason { get; set; }
}

public record DeletionRequestBody
{
    public string? Reason { get; set; }
    /// <summary>The account owner's email, typed by the operator to confirm.</summary>
    public string? ConfirmEmail { get; set; }
}

public record CompanyDeletionRequestBody
{
    public string? Reason { get; set; }
    /// <summary>The company's name, typed by the operator to confirm.</summary>
    public string? ConfirmName { get; set; }
}

public record ApproveDeletionBody
{
    /// <summary>For an account: the owner's email.</summary>
    public string? ConfirmEmail { get; set; }
    /// <summary>For a company: the company's name.</summary>
    public string? ConfirmName { get; set; }
}

public record ExtendPeriodRequest
{
    public int Days { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Owner accounts for the Platform Admin Console: search, status and detail, plus the operator
/// actions on an account's subscription. Every action needs a platform permission; the actions
/// need a reason, which is kept in the billing history with the operator's email.
/// (Plan, trial, payments, overrides and discounts of an account live on PlatformBillingController.)
/// </summary>
[Route("api/v{version:apiVersion}/platform/accounts")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformAccountsController : ControllerBase
{
    private readonly PlatformDirectoryService _directory;
    private readonly PlatformAccountActionsService _actions;
    private readonly AccountDeletionRequestService _deletionRequests;
    private readonly PlatformAccountDeletionService _deletion;

    public PlatformAccountsController(
        PlatformDirectoryService directory,
        PlatformAccountActionsService actions,
        AccountDeletionRequestService deletionRequests,
        PlatformAccountDeletionService deletion)
    {
        _directory = directory;
        _actions = actions;
        _deletionRequests = deletionRequests;
        _deletion = deletion;
    }

    private string Actor => PlatformActor.From(User);

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(PagedResult<AccountListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AccountQuery query, CancellationToken token) =>
        Ok(await _directory.ListAccountsAsync(query, token));

    [HttpGet("{ownerUserId:guid}")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(AccountDetail), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _directory.GetAccountAsync(ownerUserId, token)));

    [HttpPost("{ownerUserId:guid}/suspend")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> Suspend(Guid ownerUserId, [FromBody] ReasonRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.SuspendAsync(ownerUserId, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("{ownerUserId:guid}/reinstate")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> Reinstate(Guid ownerUserId, [FromBody] ReasonRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.ReinstateAsync(ownerUserId, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Shows or hides the public plans and storage offerings for this account (a client on a custom plan).</summary>
    [HttpPost("{ownerUserId:guid}/public-offering")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> SetPublicOffering(Guid ownerUserId, [FromBody] PublicOfferingRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.SetPublicOfferingVisibilityAsync(ownerUserId, request.Show, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("{ownerUserId:guid}/cancel")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> Cancel(Guid ownerUserId, [FromBody] ReasonRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.CancelNowAsync(ownerUserId, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("{ownerUserId:guid}/extend-period")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> ExtendPeriod(Guid ownerUserId, [FromBody] ExtendPeriodRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.ExtendPeriodAsync(ownerUserId, request.Days, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Holds the account's data back from deletion (pending audit, protest, refund claim, case).</summary>
    [HttpPost("{ownerUserId:guid}/legal-hold")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> SetLegalHold(Guid ownerUserId, [FromBody] ReasonRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.SetLegalHoldAsync(ownerUserId, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("{ownerUserId:guid}/legal-hold/release")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    public Task<IActionResult> ReleaseLegalHold(Guid ownerUserId, [FromBody] ReasonRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.ReleaseLegalHoldAsync(ownerUserId, request.Reason, Actor, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>The account's latest deletion request (any outcome); 204 when it never had one.</summary>
    [HttpGet("{ownerUserId:guid}/deletion-request")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(AccountDeletionRequestView), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeletionRequest(Guid ownerUserId, CancellationToken token) =>
        await _deletionRequests.GetLatestAsync(ownerUserId, token) is { } request ? Ok(request) : NoContent();

    /// <summary>Asks for the account to be erased for good. Nothing is deleted until a second operator approves.</summary>
    [HttpPost("{ownerUserId:guid}/deletion-requests")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    [ProducesResponseType(typeof(AccountDeletionRequestView), StatusCodes.Status200OK)]
    public Task<IActionResult> RequestDeletion(Guid ownerUserId, [FromBody] DeletionRequestBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var request = await _deletionRequests.RequestAsync(
                ownerUserId, body.Reason, body.ConfirmEmail, PlatformActor.Operator(User), token);
            await _deletionRequests.CommitChangesAsync(token);
            return Ok(request);
        });

    [HttpPost("{ownerUserId:guid}/deletion-requests/{requestId:guid}/cancel")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> CancelDeletion(Guid ownerUserId, Guid requestId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _deletionRequests.CancelAsync(ownerUserId, requestId, PlatformActor.Operator(User), token);
            await _deletionRequests.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Asks again for the companies whose data hasn't been reported deleted.</summary>
    [HttpPost("{ownerUserId:guid}/hard-delete/retry")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> RetryHardDelete(Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _deletion.RetryAsync(ownerUserId, Actor, token);
            await _deletion.CommitChangesAsync(token);
            return Accepted();
        });
}

/// <summary>
/// Pending account deletions across the console: the second operator approves or rejects here. The
/// operator who asked can't approve their own request.
/// </summary>
[Route("api/v{version:apiVersion}/platform/deletion-requests")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformDeletionRequestsController : ControllerBase
{
    private readonly AccountDeletionRequestService _requests;
    private readonly DeletionImpactService _impact;

    public PlatformDeletionRequestsController(AccountDeletionRequestService requests, DeletionImpactService impact)
    {
        _requests = requests;
        _impact = impact;
    }

    /// <summary>Whose logins approving this request deletes, and whose it keeps because they belong to other companies.</summary>
    [HttpGet("{requestId:guid}/impact")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    [ProducesResponseType(typeof(DeletionImpact), StatusCodes.Status200OK)]
    public Task<IActionResult> Impact(Guid requestId, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _impact.GetAsync(requestId, token)));

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDeletionRequestView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(CancellationToken token) => Ok(await _requests.ListPendingAsync(token));

    /// <summary>Approves and starts the erasure at once; the account shows as being deleted until every company reports back.</summary>
    [HttpPost("{requestId:guid}/approve")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> Approve(Guid requestId, [FromBody] ApproveDeletionBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var approved = await _requests.ApproveAsync(requestId, body.ConfirmName ?? body.ConfirmEmail, PlatformActor.Operator(User), token);
            await _requests.CommitChangesAsync(token);
            return Accepted(new { approved.OwnerUserId, approved.TenantId });
        });

    [HttpPost("{requestId:guid}/reject")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> Reject(Guid requestId, [FromBody] ReasonRequest body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _requests.RejectAsync(requestId, body.Reason, PlatformActor.Operator(User), token);
            await _requests.CommitChangesAsync(token);
            return Ok();
        });
}

/// <summary>Companies across all accounts, for the console's companies screen.</summary>
[Route("api/v{version:apiVersion}/platform/companies")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformCompaniesController : ControllerBase
{
    private readonly PlatformDirectoryService _directory;
    private readonly AccountDeletionRequestService _deletionRequests;
    private readonly PlatformCompanyDeletionService _deletion;

    public PlatformCompaniesController(
        PlatformDirectoryService directory, AccountDeletionRequestService deletionRequests, PlatformCompanyDeletionService deletion)
    {
        _directory = directory;
        _deletionRequests = deletionRequests;
        _deletion = deletion;
    }

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(PagedResult<CompanyListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CompanyQuery query, CancellationToken token) =>
        Ok(await _directory.ListCompaniesAsync(query, token));

    [HttpGet("{tenantId:guid}")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(CompanyListItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken token) =>
        await _directory.GetCompanyAsync(tenantId, token) is { } company ? Ok(company) : NotFound();

    /// <summary>The company's latest deletion request (any outcome); 204 when it never had one.</summary>
    [HttpGet("{tenantId:guid}/deletion-request")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(AccountDeletionRequestView), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeletionRequest(Guid tenantId, CancellationToken token) =>
        await _deletionRequests.GetLatestForCompanyAsync(tenantId, token) is { } request ? Ok(request) : NoContent();

    /// <summary>Asks for this one company to be erased for good (the account stays). Nothing is deleted until a second operator approves.</summary>
    [HttpPost("{tenantId:guid}/deletion-requests")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    [ProducesResponseType(typeof(AccountDeletionRequestView), StatusCodes.Status200OK)]
    public Task<IActionResult> RequestDeletion(Guid tenantId, [FromBody] CompanyDeletionRequestBody body, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var request = await _deletionRequests.RequestCompanyAsync(tenantId, body.Reason, body.ConfirmName, PlatformActor.Operator(User), token);
            await _deletionRequests.CommitChangesAsync(token);
            return Ok(request);
        });

    [HttpPost("{tenantId:guid}/deletion-requests/{requestId:guid}/cancel")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> CancelDeletion(Guid tenantId, Guid requestId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _deletionRequests.CancelCompanyAsync(tenantId, requestId, PlatformActor.Operator(User), token);
            await _deletionRequests.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Asks again for a company whose erasure failed or never reported back.</summary>
    [HttpPost("{tenantId:guid}/hard-delete/retry")]
    [PlatformAuthorize(PlatformPermissions.AccountsDelete)]
    public Task<IActionResult> RetryHardDelete(Guid tenantId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _deletion.RetryAsync(tenantId, PlatformActor.From(User), token);
            await _deletion.CommitChangesAsync(token);
            return Accepted();
        });
}

/// <summary>Recorded payments across all accounts, voiding one recorded in error, and refunding an online one.</summary>
[Route("api/v{version:apiVersion}/platform/payments")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformPaymentsController : ControllerBase
{
    private readonly PlatformDirectoryService _directory;
    private readonly PlatformAccountActionsService _actions;
    private readonly PaymentReconcileService _reconcile;

    public PlatformPaymentsController(PlatformDirectoryService directory, PlatformAccountActionsService actions, PaymentReconcileService reconcile)
    {
        _directory = directory;
        _actions = actions;
        _reconcile = reconcile;
    }

    /// <summary>Asks the payment gateway now about checkouts whose webhook may have been missed (an
    /// unpaid one that was paid, a paid one that was refunded) -- all accounts, or one.</summary>
    [HttpPost("recheck")]
    [PlatformAuthorize(PlatformPermissions.PaymentsRecord)]
    [ProducesResponseType(typeof(ReconcileSummary), StatusCodes.Status200OK)]
    public async Task<IActionResult> Recheck([FromQuery] Guid? ownerUserId, CancellationToken token) =>
        Ok(await _reconcile.RunAsync(ownerUserId, token));

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(PagedResult<PlatformPaymentView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PaymentQuery query, CancellationToken token) =>
        Ok(await _directory.ListPaymentsAsync(query, token));

    /// <summary>Acknowledges a reviewed gateway issue without changing the payment or its refund status.</summary>
    [HttpPost("{paymentId:guid}/review")]
    [PlatformAuthorize(PlatformPermissions.PaymentsRecord)]
    public Task<IActionResult> MarkReviewed(Guid paymentId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.MarkPaymentIssueReviewedAsync(paymentId, PlatformActor.From(User), token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("{paymentId:guid}/void")]
    [PlatformAuthorize(PlatformPermissions.PaymentsRecord)]
    public Task<IActionResult> Void(Guid paymentId, [FromBody] VoidPaymentRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.VoidPaymentAsync(paymentId, request.Reason, PlatformActor.From(User), request.Kind, request.RevertPlan, token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Sends a refund of the given amount to the payment gateway. Does not void the payment or touch the plan.</summary>
    [HttpPost("{paymentId:guid}/refund")]
    [PlatformAuthorize(PlatformPermissions.PaymentsRefund)]
    [ProducesResponseType(typeof(GatewayRefund), StatusCodes.Status200OK)]
    public Task<IActionResult> Refund(Guid paymentId, [FromBody] RefundPaymentRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var refund = await _actions.RefundPaymentAsync(paymentId, request.Amount, request.Reason, request.Notes, PlatformActor.From(User), token);
            await _actions.CommitChangesAsync(token);
            return Ok(refund);
        });
}
