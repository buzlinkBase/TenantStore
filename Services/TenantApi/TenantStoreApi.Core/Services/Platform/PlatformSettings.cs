using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

public enum PlatformSettingKind
{
    Integer,
    Text,
    Boolean,
}

/// <summary>
/// One platform setting an operator may change: what it's called, what values it accepts, and how
/// it reads from / writes to <see cref="SubscriptionOptions"/>. Strategy pattern: adding a setting
/// is one more entry in <see cref="PlatformSettingsCatalog"/>, with no branching anywhere else.
/// </summary>
public abstract class PlatformSettingDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public abstract PlatformSettingKind Kind { get; }
    public virtual int? Min => null;
    public virtual int? Max => null;

    /// <summary>The value this setting currently has in <paramref name="options"/>, as text.</summary>
    public abstract string Read(SubscriptionOptions options);

    /// <summary>Applies an already validated <paramref name="value"/> to <paramref name="options"/>.</summary>
    public abstract void Apply(SubscriptionOptions options, string value);

    /// <summary>Null when <paramref name="value"/> is acceptable, otherwise why it isn't.</summary>
    public abstract string? Validate(string value);
}

public sealed class IntegerSetting : PlatformSettingDefinition
{
    private readonly int _min;
    private readonly int _max;
    private readonly Func<SubscriptionOptions, int> _get;
    private readonly Action<SubscriptionOptions, int> _set;

    public IntegerSetting(int min, int max, Func<SubscriptionOptions, int> get, Action<SubscriptionOptions, int> set)
    {
        _min = min;
        _max = max;
        _get = get;
        _set = set;
    }

    public override PlatformSettingKind Kind => PlatformSettingKind.Integer;
    public override int? Min => _min;
    public override int? Max => _max;
    public override string Read(SubscriptionOptions options) => _get(options).ToString();
    public override void Apply(SubscriptionOptions options, string value) => _set(options, int.Parse(value.Trim()));

    public override string? Validate(string value) =>
        !int.TryParse(value.Trim(), out var number) ? $"{Label} must be a whole number."
        : number < _min || number > _max ? $"{Label} must be between {_min} and {_max}."
        : null;
}

public sealed class TextSetting : PlatformSettingDefinition
{
    private readonly int _maxLength;
    private readonly Func<SubscriptionOptions, string> _get;
    private readonly Action<SubscriptionOptions, string> _set;

    public TextSetting(int maxLength, Func<SubscriptionOptions, string> get, Action<SubscriptionOptions, string> set)
    {
        _maxLength = maxLength;
        _get = get;
        _set = set;
    }

    public override PlatformSettingKind Kind => PlatformSettingKind.Text;
    public override int? Max => _maxLength;
    public override string Read(SubscriptionOptions options) => _get(options);
    public override void Apply(SubscriptionOptions options, string value) => _set(options, value.Trim());

    public override string? Validate(string value) =>
        string.IsNullOrWhiteSpace(value) ? $"{Label} can't be empty."
        : value.Trim().Length > _maxLength ? $"{Label} can be at most {_maxLength} characters."
        : null;
}

/// <summary>A yes/no switch, stored as "true" / "false".</summary>
public sealed class BooleanSetting : PlatformSettingDefinition
{
    private readonly Func<SubscriptionOptions, bool> _get;
    private readonly Action<SubscriptionOptions, bool> _set;

    public BooleanSetting(Func<SubscriptionOptions, bool> get, Action<SubscriptionOptions, bool> set)
    {
        _get = get;
        _set = set;
    }

    public override PlatformSettingKind Kind => PlatformSettingKind.Boolean;
    public override string Read(SubscriptionOptions options) => _get(options) ? "true" : "false";
    public override void Apply(SubscriptionOptions options, string value) => _set(options, bool.Parse(value.Trim()));

    public override string? Validate(string value) =>
        bool.TryParse(value.Trim(), out _) ? null : $"{Label} must be Yes or No.";
}

/// <summary>
/// The settings the console can change. Everything else in <see cref="SubscriptionOptions"/> (cron
/// schedule, plan codes, enforcement switch ...) stays in configuration on purpose: those change
/// how the service runs, not how customers are billed.
/// </summary>
public static class PlatformSettingsCatalog
{
    public static readonly IReadOnlyList<PlatformSettingDefinition> All =
    [
        new IntegerSetting(1, 90, o => o.TrialDays, (o, v) => o.TrialDays = v)
        {
            Key = "subscriptions.trial-days", Label = "Trial length (days)",
            Description = "How long a new account's free trial lasts. Applies to trials started from now on.",
        },
        new IntegerSetting(1, 90, o => o.MaxTrialExtensionDays, (o, v) => o.MaxTrialExtensionDays = v)
        {
            Key = "subscriptions.max-trial-extension-days", Label = "Longest trial extension (days)",
            Description = "The most an operator may extend a trial by (once per account).",
        },
        new IntegerSetting(0, 60, o => o.GraceDays, (o, v) => o.GraceDays = v)
        {
            Key = "subscriptions.grace-days", Label = "Grace period (days)",
            Description = "Full access after a paid period ends unpaid, before the account is suspended.",
        },
        new IntegerSetting(30, 1095, o => o.PurgeAfterDays, (o, v) => o.PurgeAfterDays = v)
        {
            Key = "subscriptions.purge-after-days", Label = "Data retention after cancellation (days)",
            Description = "How long a canceled account's data is kept before it may be purged.",
        },
        new IntegerSetting(0, 365, o => o.LockedPunchRetentionDays, (o, v) => o.LockedPunchRetentionDays = v)
        {
            Key = "subscriptions.locked-punch-retention-days", Label = "Biometric punch retention while locked (days)",
            Description = "How long devices keep storing punches for a read-only company (never past its purge date).",
        },
        new TextSetting(1000, o => o.PaymentInstructions, (o, v) => o.PaymentInstructions = v)
        {
            Key = "subscriptions.payment-instructions", Label = "Payment instructions",
            Description = "Shown to an owner after they pick a plan: where to send the payment and what reference to use.",
        },
        new BooleanSetting(o => o.RequireSecondDeletionApprover, (o, v) => o.RequireSecondDeletionApprover = v)
        {
            Key = "platform.deletion.require-second-approver", Label = "Second approver for permanent deletions",
            Description = "On: deleting an account or company needs a second administrator to approve it. Turn off only for testing: "
                + "the administrator who asked can then approve their own request.",
        },
    ];

    public static PlatformSettingDefinition? Find(string key) => All.FirstOrDefault(s => s.Key == key);
}

/// <summary>Where the stored overrides come from. A seam so the cache can be tested without a database.</summary>
public interface IPlatformSettingsSource
{
    Dictionary<string, string> Load();
}

/// <summary>Reads the overrides from the database in a scope of its own (never the caller's unit of work).</summary>
public sealed class DbPlatformSettingsSource(IServiceScopeFactory scopes) : IPlatformSettingsSource
{
    public Dictionary<string, string> Load()
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
        return context.PlatformSettings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value);
    }
}

/// <summary>
/// The overrides, held in memory for a short time so the settings aren't read from the database on
/// every request. A change made through this process shows at once (<see cref="Invalidate"/>); other
/// instances pick it up when their copy expires.
/// </summary>
public sealed class PlatformSettingsCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly IPlatformSettingsSource _source;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private Dictionary<string, string> _values = new();
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PlatformSettingsCache(IPlatformSettingsSource source, TimeProvider clock)
    {
        _source = source;
        _clock = clock;
    }

    public IReadOnlyDictionary<string, string> Get()
    {
        lock (_gate)
        {
            if (_clock.GetUtcNow() < _expiresAt) return _values;
            try
            {
                _values = _source.Load();
                _expiresAt = _clock.GetUtcNow().Add(Ttl);
            }
            catch (Exception)
            {
                // The table may not exist yet (migration pending) or the database may blink: keep
                // serving the last known values, and look again shortly rather than on every call.
                _expiresAt = _clock.GetUtcNow().AddSeconds(5);
            }
            return _values;
        }
    }

    public void Invalidate()
    {
        lock (_gate) _expiresAt = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// <c>IOptions&lt;SubscriptionOptions&gt;</c> as the services see it: the configured options with the
/// operator's overrides laid on top. Decorator pattern -- every existing consumer keeps asking for
/// IOptions&lt;SubscriptionOptions&gt; and gets the live values, unchanged. Scoped, so a request sees
/// one consistent set of values from start to finish.
/// </summary>
public sealed class SettingsAwareSubscriptionOptions : IOptions<SubscriptionOptions>
{
    private readonly IOptionsFactory<SubscriptionOptions> _configured;
    private readonly PlatformSettingsCache _cache;
    private SubscriptionOptions? _value;

    public SettingsAwareSubscriptionOptions(IOptionsFactory<SubscriptionOptions> configured, PlatformSettingsCache cache)
    {
        _configured = configured;
        _cache = cache;
    }

    public SubscriptionOptions Value => _value ??= Build();

    private SubscriptionOptions Build()
    {
        var options = _configured.Create(Microsoft.Extensions.Options.Options.DefaultName);
        foreach (var (key, value) in _cache.Get())
        {
            var definition = PlatformSettingsCatalog.Find(key);
            // A stored value that no longer validates (a limit was tightened) is ignored, not applied.
            if (definition != null && definition.Validate(value) == null) definition.Apply(options, value);
        }
        return options;
    }
}
