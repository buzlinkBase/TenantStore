using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

public sealed record StorageOfferingView(
    string Code,
    string Name,
    string Description,
    int SortOrder,
    int BlockMb,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    string Currency,
    bool PricesIncludeVat,
    bool IsPublic,
    bool IsActive);

public sealed record CreateStorageOfferingCommand
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? SortOrder { get; init; }
    public int BlockMb { get; init; }
    public decimal MonthlyPrice { get; init; }
    /// <summary>Per block for a year (annual-cycle accounts). Unset: twelve months at the monthly price.</summary>
    public decimal? AnnualPrice { get; init; }
    public bool PricesIncludeVat { get; init; } = true;
}

public sealed record UpdateStorageOfferingCommand
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? SortOrder { get; init; }
    /// <summary>New purchases only -- blocks already held keep the size and price they were bought at.</summary>
    public int? BlockMb { get; init; }
    public decimal? MonthlyPrice { get; init; }
    public decimal? AnnualPrice { get; init; }
    public bool? PricesIncludeVat { get; init; }
    /// <summary>True: every account sees it. False: only the owners it is offered to.</summary>
    public bool? IsPublic { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>
/// The storage add-on catalog the platform console controls: what is for sale, and to whom --
/// every account (public) or only chosen owners (an offer each, optionally expiring). Same shape as
/// plan visibility. Doesn't commit.
/// </summary>
public class StorageOfferingCatalogService
{
    private static readonly Regex CodePattern = new("^[a-z][a-z0-9_-]{1,39}$");

    private readonly IUnitOfWorkService _uow;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly TimeProvider _clock;

    public StorageOfferingCatalogService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        TimeProvider clock)
    {
        _uow = uow;
        _subscriptions = subscriptions;
        _events = events;
        _notifier = notifier;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    public static StorageOfferingView ToView(StorageAddOnOffering o) => new(
        o.Code, o.Name, o.Description, o.SortOrder, o.BlockMb, o.MonthlyPrice, o.AnnualPrice, o.Currency,
        o.PricesIncludeVat, o.IsPublic, o.IsActive);

    // ── Platform catalog ─────────────────────────────────────────────────────────────────────

    public Task<List<StorageAddOnOffering>> GetAllAsync(CancellationToken token = default) =>
        Context.StorageAddOnOfferings.AsNoTracking().OrderBy(o => o.SortOrder).ToListAsync(token);

    /// <summary>Accounts each offering is currently offered to (live offers only), by offering id.</summary>
    public async Task<Dictionary<Guid, int>> GetOfferCountsAsync(CancellationToken token = default)
    {
        var now = Now;
        return await Context.StorageAddOnOffers.AsNoTracking()
            .Where(o => o.ExpiresAtUtc == null || o.ExpiresAtUtc > now)
            .GroupBy(o => o.OfferingId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, token);
    }

    public async Task<StorageAddOnOffering> RequireAsync(string code, CancellationToken token = default) =>
        await Context.StorageAddOnOfferings.FirstOrDefaultAsync(o => o.Code == code, token)
        ?? throw new KeyNotFoundException($"Unknown storage offering '{code}'.");

    /// <summary>A new offering starts hidden (not public), so it can be set up before customers see it.</summary>
    public async Task<StorageAddOnOffering> CreateAsync(CreateStorageOfferingCommand command, string? actor, CancellationToken token = default)
    {
        var code = command.Code.Trim().ToLowerInvariant();
        if (!CodePattern.IsMatch(code))
            throw new SubscriptionRuleException("An offering code is 2-40 lowercase letters, digits, '-' or '_', starting with a letter.");
        if (string.IsNullOrWhiteSpace(command.Name)) throw new SubscriptionRuleException("An offering needs a name.");
        if (command.BlockMb < 1) throw new SubscriptionRuleException("A block must add at least 1 MB.");
        if (command.MonthlyPrice < 0 || command.AnnualPrice < 0) throw new SubscriptionRuleException("Prices can't be negative.");
        if (await Context.StorageAddOnOfferings.AnyAsync(o => o.Code == code, token)
            || Context.StorageAddOnOfferings.Local.Any(o => o.Code == code))
            throw new SubscriptionRuleException($"An offering with code '{code}' already exists.");

        var sortOrder = command.SortOrder
            ?? (await Context.StorageAddOnOfferings.AnyAsync(token) ? await Context.StorageAddOnOfferings.MaxAsync(o => o.SortOrder, token) + 10 : 10);
        var offering = new StorageAddOnOffering
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = command.Name.Trim(),
            Description = command.Description?.Trim() ?? string.Empty,
            SortOrder = sortOrder,
            BlockMb = command.BlockMb,
            MonthlyPrice = command.MonthlyPrice,
            AnnualPrice = command.AnnualPrice ?? command.MonthlyPrice * 12,
            PricesIncludeVat = command.PricesIncludeVat,
            Currency = "PHP",
            IsPublic = false,
            IsActive = true,
        };
        await Context.StorageAddOnOfferings.AddAsync(offering, token);
        await _events.AddAsync(null, BillingEventTypes.StorageOfferingCreated,
            new { offering.Code, offering.Name, offering.BlockMb, offering.MonthlyPrice }, actor, token: token);
        return offering;
    }

    public async Task<StorageAddOnOffering> UpdateAsync(string code, UpdateStorageOfferingCommand command, string? actor, CancellationToken token = default)
    {
        var offering = await RequireAsync(code, token);
        if (command.BlockMb is < 1) throw new SubscriptionRuleException("A block must add at least 1 MB.");
        if (command.MonthlyPrice is < 0 || command.AnnualPrice is < 0) throw new SubscriptionRuleException("Prices can't be negative.");

        if (command.Name is { } name && !string.IsNullOrWhiteSpace(name)) offering.Name = name.Trim();
        if (command.Description is { } description) offering.Description = description.Trim();
        if (command.SortOrder is { } order) offering.SortOrder = order;
        if (command.BlockMb is { } blockMb) offering.BlockMb = blockMb;
        if (command.MonthlyPrice is { } price) offering.MonthlyPrice = price;
        if (command.AnnualPrice is { } annual) offering.AnnualPrice = annual;
        if (command.PricesIncludeVat is { } vat) offering.PricesIncludeVat = vat;
        if (command.IsPublic is { } isPublic) offering.IsPublic = isPublic;
        if (command.IsActive is { } isActive) offering.IsActive = isActive;

        await _events.AddAsync(null, BillingEventTypes.StorageOfferingUpdated, new { offering.Code, command }, actor, token: token);
        return offering;
    }

    // ── Visibility ───────────────────────────────────────────────────────────────────────────

    /// <summary>What one owner may buy: the active public offerings plus the active ones offered to
    /// them (until the offer expires). With no owner, only the public ones.</summary>
    public async Task<List<StorageAddOnOffering>> GetVisibleAsync(Guid? ownerUserId, CancellationToken token = default)
    {
        var owner = ownerUserId ?? Guid.Empty;
        var now = Now;
        var showPublic = await Context.AccountSubscriptions.AsNoTracking()
            .Where(s => s.OwnerUserId == owner)
            .Select(s => (bool?)s.ShowPublicOffering)
            .FirstOrDefaultAsync(token) is not false;
        var offeredIds = await Context.StorageAddOnOffers.AsNoTracking()
            .Where(o => o.OwnerUserId == owner && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now))
            .Select(o => o.OfferingId)
            .ToListAsync(token);

        return await Context.StorageAddOnOfferings.AsNoTracking()
            .Where(o => o.IsActive && ((showPublic && o.IsPublic) || offeredIds.Contains(o.Id)))
            .OrderBy(o => o.SortOrder)
            .ToListAsync(token);
    }

    /// <summary>The offering with this code, if the owner may buy it right now.</summary>
    public async Task<StorageAddOnOffering> RequireVisibleAsync(string code, Guid ownerUserId, CancellationToken token = default)
    {
        var visible = await GetVisibleAsync(ownerUserId, token);
        return visible.FirstOrDefault(o => o.Code == code)
            ?? throw new SubscriptionRuleException($"Storage offering '{code}' isn't available.");
    }

    // ── Targeted offers (platform) ───────────────────────────────────────────────────────────

    public Task<List<StorageAddOnOffer>> GetOffersAsync(string code, CancellationToken token = default) =>
        Context.StorageAddOnOffers.AsNoTracking()
            .Where(o => o.Offering.Code == code)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(token);

    /// <summary>Offers an offering to one owner, or changes when an existing offer expires. Idempotent.</summary>
    public async Task<StorageAddOnOffer> OfferAsync(
        string code, Guid ownerUserId, DateTime? expiresAtUtc, string? actor, CancellationToken token = default)
    {
        var offering = await RequireAsync(code, token);
        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");

        var offer = await Context.StorageAddOnOffers.FirstOrDefaultAsync(o => o.OfferingId == offering.Id && o.OwnerUserId == ownerUserId, token);
        if (offer == null)
        {
            offer = new StorageAddOnOffer { Id = Guid.CreateVersion7(), OfferingId = offering.Id, OwnerUserId = ownerUserId, CreatedBy = actor };
            await Context.StorageAddOnOffers.AddAsync(offer, token);
        }
        offer.ExpiresAtUtc = expiresAtUtc;

        await _events.AddAsync(subscription, BillingEventTypes.StorageOffered,
            new { offering = offering.Code, expiresAtUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageOffered, token);
        return offer;
    }

    /// <summary>Withdraws an offer. Blocks the owner already holds stay.</summary>
    public async Task RevokeOfferAsync(string code, Guid ownerUserId, string? actor, CancellationToken token = default)
    {
        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        var offer = await Context.StorageAddOnOffers.FirstOrDefaultAsync(
            o => o.Offering.Code == code && o.OwnerUserId == ownerUserId, token)
            ?? throw new KeyNotFoundException("This offering isn't offered to that account.");

        Context.StorageAddOnOffers.Remove(offer);
        await _events.AddAsync(subscription, BillingEventTypes.StorageOfferRevoked, new { offering = code }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageOfferRevoked, token);
    }
}
