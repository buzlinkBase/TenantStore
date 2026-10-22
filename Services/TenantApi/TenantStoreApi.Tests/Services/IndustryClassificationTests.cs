using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.DTOs;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Infrastructure.EntityConfigs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// A company's classification (industry, sub-industry, business type, size): the lookup lists only
/// active entries, a sub-industry must belong to the chosen industry, creation drops bad codes
/// while profile edits refuse them, and the codes survive CompanyProfile.Normalize.
/// </summary>
public class IndustryClassificationTests
{
    private static (IUnitOfWorkService Uow, IndustryCatalogService Catalog, TenantService Tenants) CreateSut()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var retail = new Industry { Id = Guid.NewGuid(), Code = "retail", Name = "Retail", SortOrder = 2 };
        var bpo = new Industry { Id = Guid.NewGuid(), Code = "bpo", Name = "BPO", SortOrder = 1 };
        var retired = new Industry { Id = Guid.NewGuid(), Code = "retired", Name = "Retired", SortOrder = 3, IsActive = false };
        uow.Context.Industries.AddRange(retail, bpo, retired);
        uow.Context.SubIndustries.AddRange(
            new SubIndustry { Id = Guid.NewGuid(), IndustryId = retail.Id, Code = "retail-grocery", Name = "Grocery", SortOrder = 1 },
            new SubIndustry { Id = Guid.NewGuid(), IndustryId = retail.Id, Code = "retail-old", Name = "Old", SortOrder = 2, IsActive = false },
            new SubIndustry { Id = Guid.NewGuid(), IndustryId = bpo.Id, Code = "bpo-voice", Name = "Voice", SortOrder = 1 });
        uow.Context.SaveChanges();

        var tenants = new TenantService(uow, null!, null!, new Mock<IPublishEndpoint>().Object, null!);
        return (uow, new IndustryCatalogService(uow), tenants);
    }

    [Fact]
    public async Task GetAllAsync_ListsActiveIndustriesInOrder_WithTheirActiveSubs()
    {
        var (_, catalog, _) = CreateSut();

        var all = await catalog.GetAllAsync();

        all.Select(x => x.Code).Should().Equal("bpo", "retail");
        all.Single(x => x.Code == "retail").SubIndustries.Select(x => x.Code).Should().Equal("retail-grocery");
    }

    [Fact]
    public async Task ResolveAsync_Strict_RejectsSubIndustryOfAnotherIndustry()
    {
        var (_, catalog, _) = CreateSut();

        var act = () => catalog.ResolveAsync(
            new TenantClassification { IndustryCode = "retail", SubIndustryCode = "bpo-voice" }, strict: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveAsync_Strict_RejectsSubIndustryWithoutIndustry()
    {
        var (_, catalog, _) = CreateSut();

        var act = () => catalog.ResolveAsync(new TenantClassification { SubIndustryCode = "retail-grocery" }, strict: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("nope", null, null, null)]
    [InlineData("retired", null, null, null)]
    [InlineData("retail", "nope", null, null)]
    [InlineData("retail", "retail-old", null, null)]
    [InlineData("retail", null, "nope", null)]
    [InlineData("retail", null, null, "nope")]
    public async Task ResolveAsync_Strict_RejectsUnknownOrRetiredCodes(string? industry, string? sub, string? type, string? size)
    {
        var (_, catalog, _) = CreateSut();

        var act = () => catalog.ResolveAsync(
            new TenantClassification { IndustryCode = industry, SubIndustryCode = sub, BusinessType = type, CompanySize = size },
            strict: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveAsync_Lenient_DropsOnlyTheBadValues()
    {
        var (_, catalog, _) = CreateSut();

        var resolved = await catalog.ResolveAsync(new TenantClassification
        {
            IndustryCode = " retail ",
            SubIndustryCode = "bpo-voice",
            BusinessType = "corporation",
            CompanySize = "huge",
        }, strict: false);

        resolved.IndustryCode.Should().Be("retail");
        resolved.SubIndustryCode.Should().BeNull();
        resolved.BusinessType.Should().Be("corporation");
        resolved.CompanySize.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_Blank_ClearsEverything()
    {
        var (_, catalog, _) = CreateSut();

        var resolved = await catalog.ResolveAsync(
            new TenantClassification { IndustryCode = " ", BusinessType = "" }, strict: true);

        resolved.IndustryCode.Should().BeNull();
        resolved.BusinessType.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_WithClassification_ReplacesIt()
    {
        var (uow, _, sut) = CreateSut();
        var tenantId = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "Acme", IndustryCode = "bpo", SubIndustryCode = "bpo-voice" });
        await uow.Context.SaveChangesAsync();

        await sut.UpdateProfileAsync(tenantId, "addr", "123", CancellationToken.None, new TenantClassification
        {
            IndustryCode = "retail",
            SubIndustryCode = "retail-grocery",
            BusinessType = "partnership",
            CompanySize = "11-50",
        });
        await uow.Context.SaveChangesAsync();

        var tenant = await uow.Context.Tenants.SingleAsync(x => x.Id == tenantId);
        tenant.IndustryCode.Should().Be("retail");
        tenant.SubIndustryCode.Should().Be("retail-grocery");
        tenant.BusinessType.Should().Be("partnership");
        tenant.CompanySize.Should().Be("11-50");
    }

    [Fact]
    public async Task UpdateProfileAsync_WithoutClassification_LeavesItAlone()
    {
        var (uow, _, sut) = CreateSut();
        var tenantId = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "Acme", IndustryCode = "bpo", CompanySize = "1-10" });
        await uow.Context.SaveChangesAsync();

        await sut.UpdateProfileAsync(tenantId, "addr", "123", CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        var tenant = await uow.Context.Tenants.SingleAsync(x => x.Id == tenantId);
        tenant.IndustryCode.Should().Be("bpo");
        tenant.CompanySize.Should().Be("1-10");
    }

    [Fact]
    public async Task UpdateProfileAsync_InvalidClassification_ChangesNothing()
    {
        var (uow, _, sut) = CreateSut();
        var tenantId = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "Acme", Address = "Old", IndustryCode = "bpo" });
        await uow.Context.SaveChangesAsync();

        var act = () => sut.UpdateProfileAsync(tenantId, "New", null, CancellationToken.None,
            new TenantClassification { IndustryCode = "nope" });

        await act.Should().ThrowAsync<InvalidOperationException>();
        var tenant = await uow.Context.Tenants.SingleAsync(x => x.Id == tenantId);
        tenant.Address.Should().Be("Old");
        tenant.IndustryCode.Should().Be("bpo");
    }

    [Fact]
    public void Normalize_KeepsAndTrimsClassificationCodes()
    {
        var profile = CompanyProfile.Normalize(new CompanyProfile
        {
            IndustryCode = " retail ",
            SubIndustryCode = "retail-grocery",
            BusinessType = " corporation",
            CompanySize = "11-50 ",
            AcquisitionSource = " other ",
            AcquisitionSourceOther = "  a friend ",
        });

        profile.Should().NotBeNull();
        profile!.IndustryCode.Should().Be("retail");
        profile.SubIndustryCode.Should().Be("retail-grocery");
        profile.BusinessType.Should().Be("corporation");
        profile.CompanySize.Should().Be("11-50");
        profile.AcquisitionSource.Should().Be("other");
        profile.AcquisitionSourceOther.Should().Be("a friend");
    }

    [Fact]
    public void Normalize_OnlyClassificationEntered_IsNotEmpty()
    {
        CompanyProfile.Normalize(new CompanyProfile { IndustryCode = "retail" }).Should().NotBeNull();
    }

    [Fact]
    public void Seed_EverySubIndustry_BelongsToASeededIndustry_AndCodesAreUnique()
    {
        var industryIds = IndustrySeed.Industries.Select(x => x.Id).ToHashSet();

        IndustrySeed.SubIndustries.Should().OnlyContain(s => industryIds.Contains(s.IndustryId));
        IndustrySeed.Industries.Select(x => x.Code).Should().OnlyHaveUniqueItems();
        IndustrySeed.SubIndustries.Select(x => x.Code).Should().OnlyHaveUniqueItems();
        IndustrySeed.SubIndustries.Select(x => x.Id).Should().OnlyHaveUniqueItems();
    }
}
