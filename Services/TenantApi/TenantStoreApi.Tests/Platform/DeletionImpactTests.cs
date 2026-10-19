using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The approving operator sees whose logins a deletion removes, and whose it keeps because they belong to other companies.</summary>
public class DeletionImpactTests
{
    private const string OwnerEmail = "owner@acme.ph";
    private static readonly PlatformOperator Alice = new(Guid.NewGuid(), "platform:alice@onepunch.test");

    private sealed class FakeUsers : IUserLookup
    {
        public Dictionary<Guid, (string? Email, string? Name)> Known { get; } = new();

        public Task<IReadOnlyDictionary<Guid, (string? Email, string? Name)>> FindAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<Guid, (string? Email, string? Name)>>(
                Known.Where(k => userIds.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value));
    }

    private sealed class Fixture
    {
        public SubscriptionTestHost Host { get; } = new();
        public FakeUsers Users { get; } = new();

        private TenantStoreAccountEraser Eraser => new(Host.Uow, new NoRows());

        public AccountDeletionRequestService Requests => new(
            Host.Uow, new PlatformDirectoryService(Host.Uow, Host.StateMachine),
            new PlatformAccountDeletionService(Host.Uow, Host.Events, Host.Notifier, Host.Publisher.Object, Eraser, Host.Clock),
            new PlatformCompanyDeletionService(Host.Uow, Host.Events, Host.Publisher.Object, Eraser, Host.Clock),
            Host.Events, AccountDeletionGuards.Default, AccountDeletionGuards.Company,
            Microsoft.Extensions.Options.Options.Create(Host.Options), Host.Clock);

        public DeletionImpactService Impact => new(Host.Uow, Users);
    }

    private sealed class NoRows : IRowEraser
    {
        public Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class => Task.FromResult(0);
    }

    [Fact]
    public async Task AnAccountsDeletion_KeepsAStaffMemberOfAnotherAccountsCompany_AndSaysWhere()
    {
        var f = new Fixture();
        var (owner, company) = await f.Host.TrialAccountAsync(OwnerEmail);
        var onlyHere = f.Host.AddMember(company, "Admin");
        var (_, jda) = await f.Host.TrialAccountAsync("boss@jda.ph");
        f.Host.Uow.Context.Tenants.Single(t => t.Id == jda).TenantName = "JDA SECURITY SERVICES INC";
        var shared = f.Host.AddMember(company, "HR");
        f.Host.AddMember(jda, "HR", userId: shared);
        f.Users.Known[shared] = ("marivic@jda.ph", "Marivic");
        var request = await f.Requests.RequestAsync(owner, "Test account", OwnerEmail, Alice);
        await f.Host.SaveAsync();

        var impact = await f.Impact.GetAsync(request.Id);

        impact.LoginsDeleted.Should().Be(2, "the owner and the admin belong only to this account");
        var kept = impact.Kept.Should().ContainSingle().Subject;
        kept.UserId.Should().Be(shared);
        kept.Email.Should().Be("marivic@jda.ph");
        kept.OtherCompanies.Should().Equal("JDA SECURITY SERVICES INC");
        onlyHere.Should().NotBe(shared);
    }

    [Fact]
    public async Task ACompanysDeletion_KeepsTheOwner_WhoStillHasTheAccountsOtherCompany()
    {
        var f = new Fixture();
        var (owner, keep) = await f.Host.TrialAccountAsync(OwnerEmail);
        var test = f.Host.AddCompany(owner, "Acme Test", OwnerEmail);
        f.Host.AddMember(test, "Admin");
        var request = await f.Requests.RequestCompanyAsync(test, "Test company", "Acme Test", Alice);
        await f.Host.SaveAsync();

        var impact = await f.Impact.GetAsync(request.Id);

        impact.LoginsDeleted.Should().Be(1);
        var kept = impact.Kept.Should().ContainSingle().Subject;
        kept.UserId.Should().Be(owner);
        kept.Email.Should().Be(OwnerEmail, "Auth Service didn't answer, so the owner membership's email is used");
        kept.OtherCompanies.Should().Equal("Acme");
        keep.Should().NotBe(test);
    }

    [Fact]
    public async Task AnUnknownRequest_IsNotFound()
    {
        var f = new Fixture();

        var act = () => f.Impact.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
