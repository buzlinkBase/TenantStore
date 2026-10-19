using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>Platform settings: the definitions, the DB-backed options decorator, the cache and the service.</summary>
public class PlatformSettingsTests
{
    private const string Operator = "platform:ops@onepunch.test";
    private const string TrialDaysKey = "subscriptions.trial-days";
    private const string GraceDaysKey = "subscriptions.grace-days";
    private const string InstructionsKey = "subscriptions.payment-instructions";
    private const string SecondApproverKey = "platform.deletion.require-second-approver";

    private sealed class FakeSource(Func<Dictionary<string, string>> load) : IPlatformSettingsSource
    {
        public int Loads { get; private set; }
        public Dictionary<string, string> Load()
        {
            Loads++;
            return load();
        }
    }

    private static IOptionsFactory<SubscriptionOptions> Configured(Action<SubscriptionOptions>? configure = null) =>
        new OptionsFactory<SubscriptionOptions>(configure == null ? [] : [new ConfigureOptions<SubscriptionOptions>(configure)], []);

    private static (PlatformSettingsService Service, PlatformSettingsCache Cache, FakeSource Source) Build(
        SubscriptionTestHost host, Action<SubscriptionOptions>? configure = null)
    {
        var source = new FakeSource(() => host.Uow.Context.PlatformSettings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value));
        var cache = new PlatformSettingsCache(source, host.Clock);
        return (new PlatformSettingsService(host.Uow, host.Events, Configured(configure), cache), cache, source);
    }

    // ── Definitions ──────────────────────────────────────────────────────────

    [Fact]
    public void EverySettingKey_IsUnique_AndReadsFromTheOptionsItAppliesTo()
    {
        PlatformSettingsCatalog.All.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        var options = new SubscriptionOptions();
        foreach (var setting in PlatformSettingsCatalog.All)
        {
            var current = setting.Read(options);
            setting.Validate(current).Should().BeNull($"{setting.Key}'s own default must be acceptable");
            setting.Apply(options, current);
            setting.Read(options).Should().Be(current);
        }
    }

    [Theory]
    [InlineData(TrialDaysKey, "0", false)]
    [InlineData(TrialDaysKey, "91", false)]
    [InlineData(TrialDaysKey, "14", true)]
    [InlineData(TrialDaysKey, "7.5", false)]
    [InlineData(TrialDaysKey, "abc", false)]
    [InlineData(GraceDaysKey, "0", true)]
    [InlineData(GraceDaysKey, "-1", false)]
    [InlineData(InstructionsKey, "   ", false)]
    [InlineData(InstructionsKey, "Pay by GCash to 0917...", true)]
    [InlineData(SecondApproverKey, "true", true)]
    [InlineData(SecondApproverKey, "False", true)]
    [InlineData(SecondApproverKey, "yes", false)]
    [InlineData(SecondApproverKey, "", false)]
    public void Validate_EnforcesEachSettingsRange(string key, string value, bool valid)
    {
        (PlatformSettingsCatalog.Find(key)!.Validate(value) == null).Should().Be(valid);
    }

    [Fact]
    public void TheSecondApproverSwitch_IsOnByDefault_AndCanBeTurnedOff()
    {
        var setting = PlatformSettingsCatalog.Find(SecondApproverKey)!;
        var options = new SubscriptionOptions();

        setting.Kind.Should().Be(PlatformSettingKind.Boolean);
        setting.Read(options).Should().Be("true");
        setting.Apply(options, "false");
        options.RequireSecondDeletionApprover.Should().BeFalse();
        setting.Read(options).Should().Be("false");
    }

    [Fact]
    public void ATextSetting_RefusesAValueOverItsLength()
    {
        PlatformSettingsCatalog.Find(InstructionsKey)!.Validate(new string('x', 1001)).Should().NotBeNull();
    }

    // ── Service ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ShowsConfiguredDefaults_UntilSomethingIsOverridden()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host, o => o.TrialDays = 14);

        var trial = (await service.GetAllAsync()).Single(s => s.Key == TrialDaysKey);

        trial.DefaultValue.Should().Be("14");
        trial.Value.Should().Be("14");
        trial.IsOverridden.Should().BeFalse();
        trial.Min.Should().Be(1);
        trial.Max.Should().Be(90);
    }

    [Fact]
    public async Task Set_StoresAnOverride_AndRecordsWhoChangedItFromWhatToWhat()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host);

        await service.SetAsync(TrialDaysKey, " 21 ", Operator);
        await host.SaveAsync();

        var trial = (await service.GetAllAsync()).Single(s => s.Key == TrialDaysKey);
        trial.Value.Should().Be("21");
        trial.DefaultValue.Should().Be("7");
        trial.IsOverridden.Should().BeTrue();
        trial.UpdatedBy.Should().Be(Operator);
        var recorded = host.Uow.Context.BillingEvents.Single(e => e.Type == "settings.changed");
        recorded.Actor.Should().Be(Operator);
        recorded.PayloadJson.Should().Contain("\"from\":\"7\"").And.Contain("\"to\":\"21\"");
    }

    [Fact]
    public async Task Set_ReplacesAnExistingOverride_WithoutAddingARow()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host);
        await service.SetAsync(GraceDaysKey, "10", Operator);
        await host.SaveAsync();

        await service.SetAsync(GraceDaysKey, "12", Operator);
        await host.SaveAsync();

        host.Uow.Context.PlatformSettings.Count(s => s.Key == GraceDaysKey).Should().Be(1);
        host.Uow.Context.PlatformSettings.Single(s => s.Key == GraceDaysKey).Value.Should().Be("12");
    }

    [Fact]
    public async Task Set_BackToTheConfiguredValue_RemovesTheOverride()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host);
        await service.SetAsync(TrialDaysKey, "21", Operator);
        await host.SaveAsync();

        await service.SetAsync(TrialDaysKey, "7", Operator);
        await host.SaveAsync();

        host.Uow.Context.PlatformSettings.Should().BeEmpty();
        (await service.GetAllAsync()).Single(s => s.Key == TrialDaysKey).IsOverridden.Should().BeFalse();
    }

    [Fact]
    public async Task Set_ToTheValueAlreadyInEffect_ChangesNothingAndRecordsNothing()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host);

        await service.SetAsync(TrialDaysKey, "7", Operator);

        host.Uow.Context.BillingEvents.Should().BeEmpty();
        host.Uow.Context.PlatformSettings.Local.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_RefusesAnUnknownKey_AndABadValue_ChangingNothing()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host);

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => service.SetAsync("made.up", "1", Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => service.SetAsync(TrialDaysKey, "500", Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => service.SetAsync(TrialDaysKey, null, Operator));
        await host.SaveAsync();
        host.Uow.Context.PlatformSettings.Should().BeEmpty();
        host.Uow.Context.BillingEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_DeletesTheOverrideAndRecordsIt_AndIsANoOpWithoutOne()
    {
        var host = new SubscriptionTestHost();
        var (service, _, _) = Build(host);
        await service.ResetAsync(TrialDaysKey, Operator);
        host.Uow.Context.BillingEvents.Should().BeEmpty();

        await service.SetAsync(TrialDaysKey, "30", Operator);
        await host.SaveAsync();
        await service.ResetAsync(TrialDaysKey, Operator);
        await host.SaveAsync();

        host.Uow.Context.PlatformSettings.Should().BeEmpty();
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == "settings.reset" && e.Actor == Operator);
    }

    // ── Options decorator & cache ────────────────────────────────────────────

    [Fact]
    public void TheDecorator_LaysOverridesOverTheConfiguredOptions()
    {
        var source = new FakeSource(() => new() { [TrialDaysKey] = "30", [InstructionsKey] = "Send to GCash" });
        var cache = new PlatformSettingsCache(source, new ManualClock(SubscriptionTestHost.Start));

        var options = new SettingsAwareSubscriptionOptions(Configured(o => { o.TrialDays = 14; o.GraceDays = 5; }), cache).Value;

        options.TrialDays.Should().Be(30, "overridden");
        options.PaymentInstructions.Should().Be("Send to GCash");
        options.GraceDays.Should().Be(5, "untouched settings keep the configured value");
    }

    [Fact]
    public void TheDecorator_IgnoresAStoredValueThatNoLongerValidates_AndAnUnknownKey()
    {
        var cache = new PlatformSettingsCache(new FakeSource(() => new() { [TrialDaysKey] = "9999", ["gone.setting"] = "1" }), new ManualClock(SubscriptionTestHost.Start));

        var options = new SettingsAwareSubscriptionOptions(Configured(), cache).Value;

        options.TrialDays.Should().Be(7);
    }

    [Fact]
    public void TheDecorator_GivesOneConsistentSetOfValuesPerScope()
    {
        var calls = 0;
        var cache = new PlatformSettingsCache(new FakeSource(() => new() { [TrialDaysKey] = (++calls).ToString() }), new ManualClock(SubscriptionTestHost.Start));
        var scoped = new SettingsAwareSubscriptionOptions(Configured(), cache);

        scoped.Value.Should().BeSameAs(scoped.Value);
    }

    [Fact]
    public void TheCache_ReadsOnce_UntilItExpires_ThenAgain()
    {
        var clock = new ManualClock(SubscriptionTestHost.Start);
        var source = new FakeSource(() => new() { [TrialDaysKey] = "10" });
        var cache = new PlatformSettingsCache(source, clock);

        cache.Get();
        cache.Get();
        source.Loads.Should().Be(1);

        clock.Advance(PlatformSettingsCache.Ttl + TimeSpan.FromSeconds(1));
        cache.Get();
        source.Loads.Should().Be(2);
    }

    [Fact]
    public void TheCache_Invalidate_MakesTheNextReadSeeTheChange()
    {
        var current = "10";
        var cache = new PlatformSettingsCache(new FakeSource(() => new() { [TrialDaysKey] = current }), new ManualClock(SubscriptionTestHost.Start));
        cache.Get()[TrialDaysKey].Should().Be("10");

        current = "20";
        cache.Get()[TrialDaysKey].Should().Be("10", "still cached");
        cache.Invalidate();

        cache.Get()[TrialDaysKey].Should().Be("20");
    }

    [Fact]
    public void TheCache_KeepsServingLastKnownValues_WhenTheDatabaseFails_AndDoesNotRetryEveryCall()
    {
        var clock = new ManualClock(SubscriptionTestHost.Start);
        var fail = false;
        var source = new FakeSource(() => fail ? throw new InvalidOperationException("db down") : new() { [TrialDaysKey] = "10" });
        var cache = new PlatformSettingsCache(source, clock);
        cache.Get();

        fail = true;
        clock.Advance(PlatformSettingsCache.Ttl + TimeSpan.FromSeconds(1));
        cache.Get()[TrialDaysKey].Should().Be("10");
        cache.Get();
        cache.Get();

        source.Loads.Should().Be(2, "one failed attempt, then it backs off");
    }

    [Fact]
    public void TheCache_WithAMissingTable_FallsBackToNoOverrides()
    {
        var cache = new PlatformSettingsCache(new FakeSource(() => throw new InvalidOperationException("no such table")), new ManualClock(SubscriptionTestHost.Start));

        cache.Get().Should().BeEmpty();
    }

    // ── It reaches the services ──────────────────────────────────────────────

    [Fact]
    public async Task ANewTrial_UsesTheOverriddenLength()
    {
        var host = new SubscriptionTestHost();
        var (service, cache, _) = Build(host);
        await service.SetAsync(TrialDaysKey, "21", Operator);
        await host.SaveAsync();
        cache.Invalidate();

        var options = new SettingsAwareSubscriptionOptions(Configured(), cache);
        var subscriptions = new AccountSubscriptionService(host.Uow, host.Events, host.Notifier, host.Usage, host.Resolver, options, host.Clock);
        var owner = Guid.NewGuid();
        host.AddCompany(owner);
        var trial = await subscriptions.StartTrialIfFirstCompanyAsync(owner, "owner@acme.ph");

        trial!.TrialEndsAtUtc.Should().Be(SubscriptionTestHost.Start.AddDays(21));
    }
}
