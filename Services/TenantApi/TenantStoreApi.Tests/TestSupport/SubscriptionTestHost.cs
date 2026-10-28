using MassTransit;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure.EntityConfigs;

namespace TenantStoreApi.Tests.TestSupport;

/// <summary>A clock tests move by hand.</summary>
public sealed class ManualClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Reports fixed usage for the keys it's given, standing in for hrms-api/hrms-adms-api.</summary>
public sealed class FakeUsageProvider(params string[] keys) : ICompanyUsageProvider
{
    public Dictionary<Guid, Dictionary<string, int>> Usage { get; } = new();
    public IReadOnlyCollection<string> Keys => keys;

    public Task<IReadOnlyList<CompanyUsage>> GetUsageAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<CompanyUsage>>(tenantIds
            .Where(Usage.ContainsKey)
            .Select(id => new CompanyUsage { TenantId = id, Used = Usage[id] })
            .ToList());
}

/// <summary>
/// The subscription services wired over one InMemory TenantContext, with the seeded plans, a
/// manual clock and a mocked publisher -- what every subscription test builds on.
/// </summary>
public sealed class SubscriptionTestHost
{
    public static readonly DateTime Start = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public IUnitOfWorkService Uow { get; }
    public ManualClock Clock { get; } = new(Start);
    public Mock<IPublishEndpoint> Publisher { get; } = new();
    public SubscriptionOptions Options { get; } = new();
    public FakeUsageProvider HrmsUsage { get; } = new(EntitlementKeys.EmployeesMax, EntitlementKeys.PayrollCore, EntitlementKeys.StorageMaxMb);
    public FakeUsageProvider AdmsUsage { get; } = new(EntitlementKeys.BiometricDevicesMax);
    public SubscriptionStateMachine StateMachine { get; } = new();

    public SubscriptionTestHost()
    {
        Uow = TenantTestContextFactory.CreateUnitOfWork();
        Uow.Context.Plans.AddRange(PlanSeed.Plans.Select(p => new Plan
        {
            Id = p.Id, Code = p.Code, Name = p.Name, Description = p.Description, SortOrder = p.SortOrder,
            IsRecommended = p.IsRecommended, IsPublic = p.IsPublic, MonthlyPrice = p.MonthlyPrice,
            AnnualPrice = p.AnnualPrice, Currency = p.Currency, PricesIncludeVat = p.PricesIncludeVat,
        }));
        Uow.Context.PlanEntitlements.AddRange(PlanSeed.Entitlements());
        Uow.Context.StorageAddOnOfferings.AddRange(StorageAddOnSeed.Offerings.Select(o => new StorageAddOnOffering
        {
            Id = o.Id, Code = o.Code, Name = o.Name, Description = o.Description, SortOrder = o.SortOrder,
            BlockMb = o.BlockMb, MonthlyPrice = o.MonthlyPrice, AnnualPrice = o.AnnualPrice, Currency = o.Currency,
            PricesIncludeVat = o.PricesIncludeVat, IsPublic = o.IsPublic, IsActive = o.IsActive,
        }));
        Uow.Context.SaveChanges();
    }

    private IOptions<SubscriptionOptions> WrappedOptions => Microsoft.Extensions.Options.Options.Create(Options);

    public EntitlementResolverService Resolver => new(Uow, StateMachine, WrappedOptions, Clock);
    public BillingEventService Events => new(Uow, Clock);
    public TenantStoreApi.Core.Services.Platform.ErasureConfirmations Confirmations => new(Events, WrappedOptions);
    public SubscriptionChangeNotifierService Notifier => new(Uow, Publisher.Object, new ClientStandingPublisher(Uow, Publisher.Object));
    public SubscriptionUsageService Usage => new(Uow, [new AdminSeatUsageProvider(Uow), HrmsUsage, AdmsUsage]);

    public AccountSubscriptionService Subscriptions =>
        new(Uow, Events, Notifier, Usage, Resolver, WrappedOptions, Clock);

    public PlanAllowanceService Allowance => new(Uow, Resolver, Usage, WrappedOptions);

    public DataPurgeService DataPurge => new(Uow, Events, Notifier, Publisher.Object, WrappedOptions, Clock);

    public SubscriptionLifecycleService Lifecycle =>
        new(Uow, StateMachine, Events, Notifier, Usage, DataPurge, Publisher.Object, WrappedOptions, Clock, Storage);

    public FakePaymentGateway Gateway { get; } = new();
    public PaymentOptions PaymentOptions { get; } = new() { ReturnBaseUrl = "https://hris.test/" };

    public StorageOfferingCatalogService StorageCatalog => new(Uow, Subscriptions, Events, Notifier, Clock);

    public StorageAddOnService Storage => new(Uow, Subscriptions, StorageCatalog, Resolver, Usage, Events, Notifier, Clock);

    public SubscriptionViewService Views => new(Subscriptions, Resolver, Usage, WrappedOptions, Clock, Gateway, Storage);

    /// <summary>A fresh unit of work over the same database each time, as each request gets one: the
    /// checkout service commits, and BuzlinkRepository's unit of work commits only once.</summary>
    public SubscriptionCheckoutService Checkouts =>
        new(TenantTestContextFactory.CreateUnitOfWork(Uow.Context), Subscriptions, Events, Gateway,
            Microsoft.Extensions.Options.Options.Create(PaymentOptions), Clock, Storage);

    /// <summary>A company owned by <paramref name="owner"/>, with its hidden Owner membership.</summary>
    public Guid AddCompany(Guid owner, string name = "Acme", string email = "owner@acme.ph", bool activated = true)
    {
        var tenantId = Guid.NewGuid();
        Uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = name, UserId = owner, ActivatedAtUtc = activated ? Clock.UtcNow : null });
        Uow.Context.Memberships.Add(new UserMembership
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TenantName = name, UserId = owner,
            IsHidden = true, InvitedEmail = email, FullName = "Owner", Status = "Active",
            Roles = [new MembershipRole { Id = Guid.NewGuid(), Role = "Owner" }],
        });
        Uow.Context.SaveChanges();
        HrmsUsage.Usage[tenantId] = new() { [EntitlementKeys.EmployeesMax] = 0, [EntitlementKeys.StorageMaxMb] = 0 };
        AdmsUsage.Usage[tenantId] = new() { [EntitlementKeys.BiometricDevicesMax] = 0 };
        return tenantId;
    }

    public Guid AddMember(Guid tenantId, string role, string status = "Active", Guid? userId = null)
    {
        var memberUserId = userId ?? Guid.NewGuid();
        Uow.Context.Memberships.Add(new UserMembership
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = memberUserId, Status = status,
            Roles = [new MembershipRole { Id = Guid.NewGuid(), Role = role }],
        });
        Uow.Context.SaveChanges();
        return memberUserId;
    }

    /// <summary>An account with a started trial and one company.</summary>
    public async Task<(Guid Owner, Guid TenantId)> TrialAccountAsync(string email = "owner@acme.ph")
    {
        var owner = Guid.NewGuid();
        var tenantId = AddCompany(owner, email: email);
        await Subscriptions.StartTrialIfFirstCompanyAsync(owner, email);
        await Uow.Context.SaveChangesAsync();
        return (owner, tenantId);
    }

    public AccountSubscription SubscriptionOf(Guid owner) =>
        Uow.Context.AccountSubscriptions.Single(s => s.OwnerUserId == owner);

    public async Task SaveAsync() => await Uow.Context.SaveChangesAsync();
}
