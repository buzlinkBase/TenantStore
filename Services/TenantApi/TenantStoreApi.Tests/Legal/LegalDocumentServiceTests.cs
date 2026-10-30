using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core.Services.Legal;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Legal;

/// <summary>The Terms as the console writes them: tokens, one draft, publishing frozen versions, and companies accepting them.</summary>
public class LegalDocumentServiceTests
{
    private const string Terms = LegalDocumentTypes.Terms;
    private const string Operator = "platform:ops@onepunch.test";

    private static LegalDocumentService Documents(SubscriptionTestHost host) =>
        new(host.Uow, host.Events, Microsoft.Extensions.Options.Options.Create(host.Options), host.Clock);

    private static TermsAcceptanceService Acceptances(SubscriptionTestHost host) =>
        new(host.Uow, Microsoft.Extensions.Options.Options.Create(host.Options), host.Clock);

    /// <summary>A host with the Terms switched live, as the acceptance tests need.</summary>
    private static SubscriptionTestHost LiveHost()
    {
        var host = new SubscriptionTestHost();
        host.Options.TermsLive = true;
        return host;
    }

    private static async Task<int> PublishAsync(SubscriptionTestHost host, string body, bool requiresReacceptance = true)
    {
        var documents = Documents(host);
        await documents.SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = body, RequiresReacceptance = requiresReacceptance }, Operator);
        await host.SaveAsync();
        var published = await documents.PublishAsync(Terms, Operator);
        await host.SaveAsync();
        return published.Version!.Value;
    }

    private static TermsAcceptor Acceptor(Guid userId) => new(userId, "owner@acme.ph", "Owner", "203.0.113.7", "test-agent");

    // ── Tokens ───────────────────────────────────────────────────────────────

    [Fact]
    public void Render_FillsEveryToken_FromTheLiveSettings()
    {
        var options = new SubscriptionOptions { LegalEntityName = "One Punch Inc.", GraceDays = 5, LiabilityCapMonths = 3 };

        var rendered = LegalTokenRenderer.Render("{{entity_name}} gives {{ grace_days }} days; cap {{liability_cap_months}}.", options);

        rendered.Should().Be("One Punch Inc. gives 5 days; cap 3.");
    }

    [Fact]
    public void Render_RefusesUnknownTokens()
    {
        var act = () => LegalTokenRenderer.Render("Hello {{nope}}", new SubscriptionOptions());

        act.Should().Throw<SubscriptionRuleException>().WithMessage("*{{nope}}*");
    }

    [Fact]
    public void Template_UsesOnlyKnownTokens()
    {
        LegalTokenRenderer.UnknownTokens(TermsOfServiceTemplate.Markdown).Should().BeEmpty();
    }

    [Fact]
    public void EveryToken_IsUnique_AndResolvesFromDefaults()
    {
        LegalTokenCatalog.All.Select(t => t.Name).Should().OnlyHaveUniqueItems();
        foreach (var token in LegalTokenCatalog.All)
            token.Resolve(new SubscriptionOptions()).Should().NotBeNullOrWhiteSpace(token.Name);
    }

    // ── Drafts and publishing ────────────────────────────────────────────────

    [Fact]
    public async Task GetDraft_WithNothingSaved_IsPrefilledFromTheTemplate()
    {
        var host = new SubscriptionTestHost();

        var draft = await Documents(host).GetDraftAsync(Terms);

        draft.IsSaved.Should().BeFalse();
        draft.BodyMarkdown.Should().Be(TermsOfServiceTemplate.Markdown);
    }

    [Fact]
    public async Task SaveDraft_Twice_KeepsOneDraft()
    {
        var host = new SubscriptionTestHost();
        var documents = Documents(host);

        await documents.SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = "First" }, Operator);
        await host.SaveAsync();
        await documents.SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = "Second" }, Operator);
        await host.SaveAsync();

        var drafts = await host.Uow.Context.LegalDocuments.Where(d => d.LegalStatus == LegalDocumentStatuses.Draft).ToListAsync();
        drafts.Should().ContainSingle().Which.BodyMarkdown.Should().Be("Second");
    }

    [Fact]
    public async Task SaveDraft_RefusesAnEmptyDocument()
    {
        var host = new SubscriptionTestHost();

        var act = () => Documents(host).SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = "  " }, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Publish_FreezesTheRenderedText_AndNumbersVersions()
    {
        var host = new SubscriptionTestHost();
        host.Options.LegalEntityName = "First Name Inc.";

        var first = await PublishAsync(host, "By {{entity_name}}");
        host.Options.LegalEntityName = "Renamed Inc.";
        var second = await PublishAsync(host, "By {{entity_name}} v2");

        first.Should().Be(1);
        second.Should().Be(2);
        var documents = Documents(host);
        (await documents.GetPublishedAsync(Terms, 1))!.RenderedMarkdown.Should().Be("By First Name Inc.");
        (await documents.GetPublishedAsync(Terms, null))!.RenderedMarkdown.Should().Be("By Renamed Inc. v2");
        host.Uow.Context.LegalDocuments.Single(d => d.Version == 1).LegalStatus.Should().Be(LegalDocumentStatuses.Superseded);
    }

    [Fact]
    public async Task Publish_WithAnUnknownToken_IsRefused_AndTheDraftStays()
    {
        var host = new SubscriptionTestHost();
        var documents = Documents(host);
        await documents.SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = "{{typo}}" }, Operator);
        await host.SaveAsync();

        var act = () => documents.PublishAsync(Terms, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
        host.Uow.Context.LegalDocuments.Single().LegalStatus.Should().Be(LegalDocumentStatuses.Draft);
    }

    [Fact]
    public async Task Publish_TheFirstVersion_MustAskForAcceptance()
    {
        var host = new SubscriptionTestHost();
        var documents = Documents(host);
        await documents.SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = "Terms", RequiresReacceptance = false }, Operator);
        await host.SaveAsync();

        var act = () => documents.PublishAsync(Terms, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Publish_WithoutADraft_IsRefused()
    {
        var host = new SubscriptionTestHost();

        var act = () => Documents(host).PublishAsync(Terms, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task GetPublished_NeverReturnsTheDraft()
    {
        var host = new SubscriptionTestHost();
        await Documents(host).SaveDraftAsync(Terms, new SaveLegalDraft { BodyMarkdown = "Draft" }, Operator);
        await host.SaveAsync();

        (await Documents(host).GetPublishedAsync(Terms, null)).Should().BeNull();
    }

    // ── Acceptance ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_BeforeAccepting_RequiresAcceptance()
    {
        var host = LiveHost();
        var tenantId = host.AddCompany(Guid.NewGuid());
        await PublishAsync(host, "Terms");

        var status = await Acceptances(host).GetStatusAsync(tenantId);

        status.CurrentVersion.Should().Be(1);
        status.AcceptedVersion.Should().BeNull();
        status.RequiresAcceptance.Should().BeTrue();
    }

    [Fact]
    public async Task Status_WithNothingPublished_RequiresNothing()
    {
        var host = LiveHost();

        var status = await Acceptances(host).GetStatusAsync(host.AddCompany(Guid.NewGuid()));

        status.RequiresAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task Accept_RecordsTheEvidence_AndIsIdempotent()
    {
        var host = LiveHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner);
        await PublishAsync(host, "Terms");
        var acceptances = Acceptances(host);

        await acceptances.AcceptAsync(tenantId, 1, Acceptor(owner));
        await host.SaveAsync();
        await acceptances.AcceptAsync(tenantId, 1, Acceptor(owner));
        await host.SaveAsync();

        var row = host.Uow.Context.TermsAcceptances.Should().ContainSingle().Subject;
        row.UserId.Should().Be(owner);
        row.IpAddress.Should().Be("203.0.113.7");
        row.AcceptedAtUtc.Should().Be(host.Clock.UtcNow);
        (await acceptances.GetStatusAsync(tenantId)).RequiresAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task Accept_AnOutdatedVersion_IsRefused()
    {
        var host = LiveHost();
        var tenantId = host.AddCompany(Guid.NewGuid());
        await PublishAsync(host, "One");
        await PublishAsync(host, "Two");

        var act = () => Acceptances(host).AcceptAsync(tenantId, 1, Acceptor(Guid.NewGuid()));

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task NewVersion_ThatAsksAgain_RequiresAcceptance_AWordingFixDoesNot()
    {
        var host = LiveHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner);
        await PublishAsync(host, "One");
        await Acceptances(host).AcceptAsync(tenantId, 1, Acceptor(owner));
        await host.SaveAsync();

        await PublishAsync(host, "One, with a typo fixed", requiresReacceptance: false);
        (await Acceptances(host).GetStatusAsync(tenantId)).RequiresAcceptance.Should().BeFalse();

        await PublishAsync(host, "Three, materially changed");
        var status = await Acceptances(host).GetStatusAsync(tenantId);
        status.CurrentVersion.Should().Be(3);
        status.AcceptedVersion.Should().Be(1);
        status.RequiresAcceptance.Should().BeTrue();
    }

    [Fact]
    public async Task List_ShowsTheCompanyName_AndFiltersByVersion()
    {
        var host = LiveHost();
        var owner = Guid.NewGuid();
        var tenantId = host.AddCompany(owner, name: "Acme Security");
        await PublishAsync(host, "One");
        await Acceptances(host).AcceptAsync(tenantId, 1, Acceptor(owner));
        await host.SaveAsync();

        var page = await Acceptances(host).ListAsync(new TermsAcceptanceQuery { Version = 1, Search = "Acme" });
        var none = await Acceptances(host).ListAsync(new TermsAcceptanceQuery { Version = 2 });

        page.Items.Should().ContainSingle().Which.CompanyName.Should().Be("Acme Security");
        none.Total.Should().Be(0);
    }

    // ── The live switch ──────────────────────────────────────────────────────

    [Fact]
    public async Task NotLive_RequiresNothing_EvenWithAPublishedVersion()
    {
        var host = new SubscriptionTestHost();
        var tenantId = host.AddCompany(Guid.NewGuid());
        await PublishAsync(host, "Terms");

        var status = await Acceptances(host).GetStatusAsync(tenantId);

        status.IsLive.Should().BeFalse();
        status.CurrentVersion.Should().Be(1);
        status.RequiresAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task NotLive_AcceptingIsRefused()
    {
        var host = new SubscriptionTestHost();
        var tenantId = host.AddCompany(Guid.NewGuid());
        await PublishAsync(host, "Terms");

        var act = () => Acceptances(host).AcceptAsync(tenantId, 1, Acceptor(Guid.NewGuid()));

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*in effect*");
    }

    [Fact]
    public void TheSwitch_IsOffByDefault_AndReportedByTheDocumentService()
    {
        var host = new SubscriptionTestHost();

        Documents(host).IsLive.Should().BeFalse();
        host.Options.TermsLive = true;
        Documents(host).IsLive.Should().BeTrue();
    }
}
