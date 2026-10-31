using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// Company details entered at creation: blanks are dropped, the console-facing ones (address,
/// contact number) are kept on the Tenant row, and later edits from HRIS Company Info keep them
/// in sync.
/// </summary>
public class TenantProfileTests
{
    private static (TenantService Sut, IUnitOfWorkService Uow) CreateSut()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        return (new TenantService(uow, null!, null!, new Mock<IPublishEndpoint>().Object, null!), uow);
    }

    [Fact]
    public void Normalize_NothingEntered_IsNull()
    {
        CompanyProfile.Normalize(new CompanyProfile { Address = "  ", Tin = "" }).Should().BeNull();
        CompanyProfile.Normalize(null).Should().BeNull();
    }

    [Fact]
    public void Normalize_TrimsValues_AndBlanksBecomeNull()
    {
        var profile = CompanyProfile.Normalize(new CompanyProfile { Address = "  1 Main St ", Tin = "   " });

        profile.Should().NotBeNull();
        profile!.Address.Should().Be("1 Main St");
        profile.Tin.Should().BeNull();
    }

    [Fact]
    public void Normalize_KeepsThePayrollPolicyChoice()
    {
        var profile = CompanyProfile.Normalize(new CompanyProfile
        {
            OtNdCalculationMethod = " Additive ",
            AcknowledgeOtNdDeviation = true,
        });

        profile.Should().NotBeNull();
        profile!.OtNdCalculationMethod.Should().Be("Additive");
        profile.AcknowledgeOtNdDeviation.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateProfileAsync_SetsAddressAndContact_BlankClears()
    {
        var (sut, uow) = CreateSut();
        var tenantId = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "Acme", Address = "Old", Contact = "123" });
        await uow.Context.SaveChangesAsync();

        await sut.UpdateProfileAsync(tenantId, "  New Address ", "  ", CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        var tenant = await uow.Context.Tenants.SingleAsync(x => x.Id == tenantId);
        tenant.Address.Should().Be("New Address");
        tenant.Contact.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_UnknownTenant_IsNotFound()
    {
        var (sut, _) = CreateSut();

        var act = () => sut.UpdateProfileAsync(Guid.NewGuid(), "a", "b", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
