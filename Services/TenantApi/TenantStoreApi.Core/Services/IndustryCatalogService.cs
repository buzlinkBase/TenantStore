using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Domain.DTOs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services;

/// <summary>One industry with its sub-industries, as the company screens' dropdowns list them.</summary>
public sealed record IndustryView(string Code, string Name, IReadOnlyList<SubIndustryView> SubIndustries);
public sealed record SubIndustryView(string Code, string Name);

/// <summary>
/// The industry / sub-industry lookup. Read side feeds the create-company and Company Info
/// dropdowns; <see cref="ResolveAsync"/> is the single place a company's classification codes are
/// checked, so Tenant Service never stores a code the lookup doesn't know.
/// </summary>
public class IndustryCatalogService
{
    private readonly IUnitOfWorkService _uow;

    public IndustryCatalogService(IUnitOfWorkService uow) => _uow = uow;

    private TenantContext Context => _uow.Context;

    /// <summary>Active industries in display order, each with its active sub-industries.</summary>
    public async Task<List<IndustryView>> GetAllAsync(CancellationToken token = default)
    {
        var industries = await Context.Industries.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(token);
        var subs = await Context.SubIndustries.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(token);

        return industries
            .Select(i => new IndustryView(
                i.Code,
                i.Name,
                subs.Where(s => s.IndustryId == i.Id).Select(s => new SubIndustryView(s.Code, s.Name)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Checks a classification and returns it normalized (trimmed, blanks to null). The industry and
    /// business lists must know the codes, and a sub-industry needs an industry it belongs to.
    /// <paramref name="strict"/> throws <see cref="InvalidOperationException"/> on a bad value (an
    /// owner editing the profile gets told); lenient drops the bad value instead (company creation
    /// is a queued message that must not fail over an optional field).
    /// </summary>
    public async Task<TenantClassification> ResolveAsync(TenantClassification? input, bool strict, CancellationToken token = default)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var industryCode = Clean(input?.IndustryCode);
        var subCode = Clean(input?.SubIndustryCode);
        var businessType = Clean(input?.BusinessType);
        var size = Clean(input?.CompanySize);

        if (industryCode != null)
        {
            var known = await Context.Industries.AsNoTracking()
                .AnyAsync(x => x.Code == industryCode && x.IsActive, token);
            if (!known)
            {
                if (strict) throw new InvalidOperationException("Unknown industry.");
                industryCode = null;
            }
        }

        if (subCode != null)
        {
            var belongs = industryCode != null && await Context.SubIndustries.AsNoTracking()
                .AnyAsync(x => x.Code == subCode && x.IsActive && x.Industry.Code == industryCode, token);
            if (!belongs)
            {
                if (strict) throw new InvalidOperationException("The sub-industry doesn't belong to the selected industry.");
                subCode = null;
            }
        }

        if (businessType != null && !CompanyClassification.IsBusinessType(businessType))
        {
            if (strict) throw new InvalidOperationException("Unknown business type.");
            businessType = null;
        }

        if (size != null && !CompanyClassification.IsCompanySize(size))
        {
            if (strict) throw new InvalidOperationException("Unknown company size.");
            size = null;
        }

        return new TenantClassification
        {
            IndustryCode = industryCode,
            SubIndustryCode = subCode,
            BusinessType = businessType,
            CompanySize = size,
        };
    }
}
