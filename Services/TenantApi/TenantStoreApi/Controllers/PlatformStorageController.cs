using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

/// <summary>A storage offering as the console lists it: its definition plus how many accounts it is offered to.</summary>
public sealed record PlatformStorageOfferingView(StorageOfferingView Offering, int OfferedTo);

public record GrantStorageRequest
{
    public string OfferingCode { get; set; } = string.Empty;
    public int Blocks { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// The storage add-on catalog for the Platform Admin Console. The console decides what is for sale and
/// to whom: an offering is public (every account sees it) or targeted (only the accounts it is
/// offered to). It can also give an account free blocks. Offerings are never deleted (held blocks
/// reference them); a retired one is made inactive.
/// </summary>
[Route("api/v{version:apiVersion}/platform/storage")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformStorageController : ControllerBase
{
    private readonly StorageOfferingCatalogService _catalog;
    private readonly StorageAddOnService _addOns;
    private readonly PlatformDirectoryService _directory;

    public PlatformStorageController(
        StorageOfferingCatalogService catalog,
        StorageAddOnService addOns,
        PlatformDirectoryService directory)
    {
        _catalog = catalog;
        _addOns = addOns;
        _directory = directory;
    }

    private string Actor => PlatformActor.From(User);

    // ── Catalog ──────────────────────────────────────────────────────────────────────────────

    [HttpGet("offerings")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(List<PlatformStorageOfferingView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token)
    {
        var offerings = await _catalog.GetAllAsync(token);
        var offered = await _catalog.GetOfferCountsAsync(token);
        return Ok(offerings.Select(o => new PlatformStorageOfferingView(
            StorageOfferingCatalogService.ToView(o), offered.GetValueOrDefault(o.Id))).ToList());
    }

    [HttpPost("offerings")]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(typeof(StorageOfferingView), StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] CreateStorageOfferingCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var offering = await _catalog.CreateAsync(command, Actor, token);
            await _catalog.CommitChangesAsync(token);
            return Ok(StorageOfferingCatalogService.ToView(offering));
        });

    [HttpPatch("offerings/{code}")]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(typeof(StorageOfferingView), StatusCodes.Status200OK)]
    public Task<IActionResult> Update(string code, [FromBody] UpdateStorageOfferingCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var offering = await _catalog.UpdateAsync(code, command, Actor, token);
            await _catalog.CommitChangesAsync(token);
            return Ok(StorageOfferingCatalogService.ToView(offering));
        });

    // ── Targeted offers ──────────────────────────────────────────────────────────────────────

    /// <summary>The accounts an offering is offered to (it matters when the offering isn't public).</summary>
    [HttpGet("offerings/{code}/offers")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(List<PlanOfferView>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetOffers(string code, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var offers = await _catalog.GetOffersAsync(code, token);
            var identities = await _directory.OwnerIdentitiesAsync(offers.Select(o => o.OwnerUserId).Distinct().ToList(), token);
            return Ok(offers.Select(o =>
            {
                identities.TryGetValue(o.OwnerUserId, out var identity);
                return new PlanOfferView(o.Id, o.OwnerUserId, identity.Email, identity.Name, o.ExpiresAtUtc, o.CreatedBy, o.CreatedAt);
            }).ToList());
        });

    [HttpPut("offerings/{code}/offers/{ownerUserId:guid}")]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(typeof(PlanOfferView), StatusCodes.Status200OK)]
    public Task<IActionResult> Offer(string code, Guid ownerUserId, [FromBody] OfferPlanRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var offer = await _catalog.OfferAsync(code, ownerUserId, request.ExpiresAtUtc, Actor, token);
            await _catalog.CommitChangesAsync(token);
            var identities = await _directory.OwnerIdentitiesAsync([ownerUserId], token);
            identities.TryGetValue(ownerUserId, out var identity);
            return Ok(new PlanOfferView(offer.Id, offer.OwnerUserId, identity.Email, identity.Name, offer.ExpiresAtUtc, offer.CreatedBy, offer.CreatedAt));
        });

    /// <summary>Withdraw an offer. Blocks the account already holds stay.</summary>
    [HttpDelete("offerings/{code}/offers/{ownerUserId:guid}")]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Revoke(string code, Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _catalog.RevokeOfferAsync(code, ownerUserId, Actor, token);
            await _catalog.CommitChangesAsync(token);
            return Ok();
        });

    // ── One account's storage ────────────────────────────────────────────────────────────────

    /// <summary>The account's storage: base capacity, held blocks, usage across all its companies.</summary>
    [HttpGet("accounts/{ownerUserId:guid}")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(StorageSummary), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAccount(Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _addOns.GetSummaryAsync(ownerUserId, token)));

    /// <summary>Give an account free blocks.</summary>
    [HttpPost("accounts/{ownerUserId:guid}/grants")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public Task<IActionResult> Grant(Guid ownerUserId, [FromBody] GrantStorageRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var addOn = await _addOns.GrantAsync(ownerUserId, request.OfferingCode, request.Blocks, request.Reason, Actor, token);
            await _addOns.CommitChangesAsync(token);
            return Ok(addOn.Id);
        });

    /// <summary>Remove held blocks outright.</summary>
    [HttpDelete("accounts/{ownerUserId:guid}/add-ons/{addOnId:guid}")]
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Remove(Guid ownerUserId, Guid addOnId, [FromQuery] string? reason, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _addOns.RemoveAsync(ownerUserId, addOnId, reason, Actor, token);
            await _addOns.CommitChangesAsync(token);
            return Ok();
        });
}
