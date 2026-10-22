using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The industry, business type, size and "how did you hear" the platform console shows per company.</summary>
public class PlatformCompanyClassificationTests
{
    private sealed class NoActivity : ITenantActivitySource
    {
        public Task<IReadOnlyDictionary<Guid, DateTime>> GetLastActiveAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(new Dictionary<Guid, DateTime>());
    }

    private static PlatformDirectoryService Directory(SubscriptionTestHost host) =>
        new(host.Uow, host.StateMachine, new NoActivity());

    private static (SubscriptionTestHost Host, Guid Retail, Guid Plain) Setup()
    {
        var host = new SubscriptionTestHost();
        var industry = new Industry { Id = Guid.NewGuid(), Code = "retail", Name = "Retail & Wholesale" };
        host.Uow.Context.Industries.Add(industry);
        host.Uow.Context.SubIndustries.Add(new SubIndustry
        {
            Id = Guid.NewGuid(), IndustryId = industry.Id, Code = "retail-grocery", Name = "Grocery & Supermarket",
        });
        var owner = Guid.NewGuid();
        var retail = host.AddCompany(owner, "Retail Co", "boss@acme.ph");
        var plain = host.AddCompany(owner, "Plain Co", "boss@acme.ph");
        var tenant = host.Uow.Context.Tenants.Single(t => t.Id == retail);
        tenant.IndustryCode = "retail";
        tenant.SubIndustryCode = "retail-grocery";
        tenant.BusinessType = "corporation";
        tenant.CompanySize = "11-50";
        tenant.AcquisitionSource = "other";
        tenant.AcquisitionSourceOther = "a friend";
        host.Uow.Context.SaveChanges();
        return (host, retail, plain);
    }

    [Fact]
    public async Task Companies_CarryClassification_WithIndustryNamesResolved()
    {
        var (host, _, _) = Setup();

        var page = await Directory(host).ListCompaniesAsync(new CompanyQuery());

        var company = page.Items.Single(c => c.Name == "Retail Co");
        company.IndustryCode.Should().Be("retail");
        company.IndustryName.Should().Be("Retail & Wholesale");
        company.SubIndustryName.Should().Be("Grocery & Supermarket");
        company.BusinessType.Should().Be("corporation");
        company.CompanySize.Should().Be("11-50");
        company.AcquisitionSource.Should().Be("other");
        company.AcquisitionSourceOther.Should().Be("a friend");

        var plain = page.Items.Single(c => c.Name == "Plain Co");
        plain.IndustryCode.Should().BeNull();
        plain.IndustryName.Should().BeNull();
    }

    [Fact]
    public async Task Company_WithACodeTheLookupNoLongerHas_ShowsTheCodeAsItsName()
    {
        var (host, retail, _) = Setup();
        host.Uow.Context.Tenants.Single(t => t.Id == retail).IndustryCode = "gone";
        host.Uow.Context.SaveChanges();

        var company = await Directory(host).GetCompanyAsync(retail);

        company!.IndustryName.Should().Be("gone");
    }

    [Fact]
    public async Task IndustryFilter_ListsOnlyThatIndustry()
    {
        var (host, _, _) = Setup();

        var page = await Directory(host).ListCompaniesAsync(new CompanyQuery(IndustryCode: "retail"));

        page.Items.Select(c => c.Name).Should().Equal("Retail Co");
    }
}
