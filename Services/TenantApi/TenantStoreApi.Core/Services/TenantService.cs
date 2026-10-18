
using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Exceptions;

namespace TenantStoreApi.Core.Services;

public class TenantService : BaseService<Tenant>
{
    private readonly IMapper _mapper;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPublishEndpoint _publisher;
    public TenantService(IUnitOfWorkService service,
        IMapper mapper,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher,

        PasswordCrypto crypto) : base(service)
    {
        _mapper = mapper;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
    }

    // The company name is owned by HRIS's Company Info screen; this keeps every copy Tenant
    // Service holds in line with it -- the Tenant row itself, each member's UserMembership
    // (what the company switcher lists), and delegations naming this tenant as the guest -- then
    // tells Auth Service (TenantRenamed) to update its own copies and refresh members' sessions.
    // Doesn't commit; the caller owns the transaction, like the rest of this service.
    public async Task RenameAsync(Guid tenantId, string newName, CancellationToken token)
    {
        var name = newName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Company name is required.");

        var tenant = await Context.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, token)
            ?? throw new KeyNotFoundException("Company not found.");

        tenant.TenantName = name;

        // Every membership keeps the name in sync, but only staff get the live "renamed" push --
        // client users have no HRIS session to refresh.
        var memberships = await Context.Memberships
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(token);
        foreach (var membership in memberships)
            membership.TenantName = name;
        var staff = memberships.Where(x => !x.IsClientUser()).ToList();

        var delegations = await Context.TenantDelegations
            .Where(x => x.GuestTenantId == tenantId)
            .ToListAsync(token);
        foreach (var delegation in delegations)
            delegation.GuestTenantName = name;

        await _publisher.Publish(new TenantRenamed
        {
            TenantId = tenantId,
            TenantName = name,
            MemberUserIds = staff
                .Where(x => x.UserId != Guid.Empty)
                .Select(x => x.UserId)
                .Distinct()
                .ToList(),
        }, token);
    }

    // Keeps the console-facing contact details in line with HRIS's Company Info screen. Blank
    // clears the value. Doesn't commit; the caller owns the transaction.
    public async Task UpdateProfileAsync(Guid tenantId, string? address, string? contact, CancellationToken token)
    {
        var tenant = await Context.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, token)
            ?? throw new KeyNotFoundException("Company not found.");

        tenant.Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        tenant.Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
    }

    protected override async Task<EvaluationResult> CreateValidatorAsync(Tenant model, CancellationToken token)
    {
        await base.CreateValidatorAsync(model, token);
        Guard.ThrowIfNull(model, "Account Payload");
        return EvaluationResult.OK;
    }

    public async Task<TenantModel> CreateTenant(TenantCreationRequested payload, CancellationToken token)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Id = payload.TenantId;
        var profile = CompanyProfile.Normalize(payload.Profile);
        tenant.Address = profile?.Address;
        tenant.Contact = profile?.Contact;
        await CreateAsync(tenant, token);
        await Uow.SaveChangesAsync(token);
        _tenantProvider.SetTenantId(tenant.Id);
        return _mapper.Map<TenantModel>(tenant);
    }
    public async Task UpdateAsync(Guid Id, UpdateTenant payload, CancellationToken token)
    {
        var tenant = _mapper.Map<Tenant>(payload);
        tenant.Id = Id;
        await CreateOrUpdateAsync(tenant, token);
        CommitChanges();
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken token)
    {
        await CreateOrUpdateAsync(tenant, token);
    }

    public async Task<Tenant?> FindTenantAsync(Guid Id, CancellationToken token)
    {
        var tenant = await Repository.FindOneAsync<Tenant>(Id, token);
        return tenant;
    }

    public async Task<List<Tenant>> FindAlltenants(Guid userId, CancellationToken token)
    {
        return await GetQueryable(x => x.UserId == userId)
            .ToListAsync(token);
    }

    public async Task DeleteAsync(IEnumerable<Tenant> tenants, CancellationToken token)
    {
        await RemoveRangeAsync(tenants, token);
    }
}