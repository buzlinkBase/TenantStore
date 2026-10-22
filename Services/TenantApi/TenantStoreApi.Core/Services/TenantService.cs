
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
    // clears the value. Classification (industry, sub-industry, business type, size) is replaced as
    // a whole when sent and left alone when null -- an invalid code is refused with an
    // InvalidOperationException before anything changes. Doesn't commit; the caller owns the
    // transaction.
    public async Task UpdateProfileAsync(Guid tenantId, string? address, string? contact, CancellationToken token,
        TenantClassification? classification = null)
    {
        var tenant = await Context.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, token)
            ?? throw new KeyNotFoundException("Company not found.");

        var resolved = classification == null
            ? null
            : await new IndustryCatalogService(Uow).ResolveAsync(classification, strict: true, token);

        tenant.Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        tenant.Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();

        if (resolved != null)
        {
            tenant.IndustryCode = resolved.IndustryCode;
            tenant.SubIndustryCode = resolved.SubIndustryCode;
            tenant.BusinessType = resolved.BusinessType;
            tenant.CompanySize = resolved.CompanySize;
        }
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

        // Optional classification: an unknown code is dropped rather than failing the queued
        // company creation over a field nobody needs to have.
        var classification = await new IndustryCatalogService(Uow).ResolveAsync(new TenantClassification
        {
            IndustryCode = profile?.IndustryCode,
            SubIndustryCode = profile?.SubIndustryCode,
            BusinessType = profile?.BusinessType,
            CompanySize = profile?.CompanySize,
        }, strict: false, token);
        tenant.IndustryCode = classification.IndustryCode;
        tenant.SubIndustryCode = classification.SubIndustryCode;
        tenant.BusinessType = classification.BusinessType;
        tenant.CompanySize = classification.CompanySize;

        if (CompanyClassification.IsAcquisitionSource(profile?.AcquisitionSource))
        {
            tenant.AcquisitionSource = profile!.AcquisitionSource;
            tenant.AcquisitionSourceOther = profile.AcquisitionSource == CompanyClassification.OtherSource
                ? Truncate(profile.AcquisitionSourceOther, 200)
                : null;
        }

        await CreateAsync(tenant, token);
        await Uow.SaveChangesAsync(token);
        _tenantProvider.SetTenantId(tenant.Id);
        return _mapper.Map<TenantModel>(tenant);
    }
    private static string? Truncate(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
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