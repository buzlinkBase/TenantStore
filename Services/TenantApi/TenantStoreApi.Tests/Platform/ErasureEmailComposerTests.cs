using FluentAssertions;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Platform;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

public class ErasureEmailComposerTests
{
    private static readonly DateTime DeletedAt = new(2026, 10, 31, 5, 4, 0, DateTimeKind.Utc);

    [Fact]
    public void ForAccount_NamesTheCompanies_AndShowsPhilippineTime()
    {
        var email = ErasureEmailComposer.ForAccount("owner@acme.ph", ["Acme", "Acme Two"], DeletedAt)!;

        email.To.Should().Be("owner@acme.ph");
        email.Subject.Should().Be("Your One Punch HRIS account has been deleted");
        email.HtmlBody.Should().Contain("Acme, Acme Two").And.Contain("1:04 PM (Philippine time)");
        email.TextBody.Should().Contain("Acme, Acme Two");
        email.Source.Should().Be("tenant");
    }

    [Fact]
    public void ForCompany_NamesTheCompany()
    {
        var email = ErasureEmailComposer.ForCompany("owner@acme.ph", "Acme Test", DeletedAt)!;

        email.Subject.Should().Contain("Acme Test");
        email.HtmlBody.Should().Contain("the company Acme Test");
    }

    [Fact]
    public void CompanyNamesAreHtmlEncoded()
    {
        var email = ErasureEmailComposer.ForCompany("owner@acme.ph", "<b>Evil</b>", DeletedAt)!;

        email.HtmlBody.Should().NotContain("<b>Evil</b>").And.Contain("&lt;b&gt;Evil&lt;/b&gt;");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutAnAddress_NothingIsComposed(string? address)
    {
        ErasureEmailComposer.ForAccount(address, ["Acme"], DeletedAt).Should().BeNull();
        ErasureEmailComposer.ForCompany(address, "Acme", DeletedAt).Should().BeNull();
    }

    [Fact]
    public void TheMessage_PassesNotificationServicesChecks()
    {
        var email = ErasureEmailComposer.ForAccount("owner@acme.ph", [], DeletedAt)!;

        SendEmailRequested.TryNormalize(email, out _, out var problem).Should().BeTrue(problem);
    }
}
