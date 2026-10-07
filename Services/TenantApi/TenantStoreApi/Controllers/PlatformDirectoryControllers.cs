using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record ReasonRequest
{
    public string? Reason { get; set; }
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
[ApiController]
public class PlatformAccountsController : ControllerBase
{
    private readonly PlatformDirectoryService _directory;
    private readonly PlatformAccountActionsService _actions;

    public PlatformAccountsController(PlatformDirectoryService directory, PlatformAccountActionsService actions)
    {
        _directory = directory;
        _actions = actions;
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
}

/// <summary>Companies across all accounts, for the console's companies screen.</summary>
[Route("api/v{version:apiVersion}/platform/companies")]
[ApiVersion("1.0")]
[ApiController]
public class PlatformCompaniesController : ControllerBase
{
    private readonly PlatformDirectoryService _directory;

    public PlatformCompaniesController(PlatformDirectoryService directory) => _directory = directory;

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(PagedResult<CompanyListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CompanyQuery query, CancellationToken token) =>
        Ok(await _directory.ListCompaniesAsync(query, token));
}

/// <summary>Recorded payments across all accounts, and voiding one recorded in error.</summary>
[Route("api/v{version:apiVersion}/platform/payments")]
[ApiVersion("1.0")]
[ApiController]
public class PlatformPaymentsController : ControllerBase
{
    private readonly PlatformDirectoryService _directory;
    private readonly PlatformAccountActionsService _actions;

    public PlatformPaymentsController(PlatformDirectoryService directory, PlatformAccountActionsService actions)
    {
        _directory = directory;
        _actions = actions;
    }

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(PagedResult<PlatformPaymentView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PaymentQuery query, CancellationToken token) =>
        Ok(await _directory.ListPaymentsAsync(query, token));

    [HttpPost("{paymentId:guid}/void")]
    [PlatformAuthorize(PlatformPermissions.PaymentsRecord)]
    public Task<IActionResult> Void(Guid paymentId, [FromBody] ReasonRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _actions.VoidPaymentAsync(paymentId, request.Reason, PlatformActor.From(User), token);
            await _actions.CommitChangesAsync(token);
            return Ok();
        });
}
