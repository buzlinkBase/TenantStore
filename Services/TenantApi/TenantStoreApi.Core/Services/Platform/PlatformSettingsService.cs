using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

public sealed record PlatformSettingView(
    string Key,
    string Label,
    string Description,
    PlatformSettingKind Kind,
    int? Min,
    int? Max,
    /// <summary>What configuration (appsettings / environment) says -- the value used when there's no override.</summary>
    string DefaultValue,
    /// <summary>What is in effect now.</summary>
    string Value,
    bool IsOverridden,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy);

/// <summary>
/// Reading and changing the platform-wide settings from the console. A change is stored as an
/// override of the configured value, takes effect for the whole platform, and is recorded in the
/// billing history with the operator's name; "reset" deletes the override so the configured value
/// applies again. Like the other platform services it doesn't commit: the caller owns the unit of work.
/// </summary>
public class PlatformSettingsService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly IOptionsFactory<SubscriptionOptions> _configured;
    private readonly PlatformSettingsCache _cache;

    public PlatformSettingsService(
        IUnitOfWorkService uow,
        BillingEventService events,
        IOptionsFactory<SubscriptionOptions> configured,
        PlatformSettingsCache cache)
    {
        _uow = uow;
        _events = events;
        _configured = configured;
        _cache = cache;
    }

    private TenantContext Context => _uow.Context;

    public async Task<bool> CommitChangesAsync(CancellationToken token = default)
    {
        var committed = await _uow.CommitChangesAsync("", token);
        // After the commit, so a request that races it can't re-cache the old values.
        _cache.Invalidate();
        return committed;
    }

    public async Task<List<PlatformSettingView>> GetAllAsync(CancellationToken token = default)
    {
        var defaults = _configured.Create(Options.DefaultName);
        var stored = await Context.PlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, token);

        return PlatformSettingsCatalog.All.Select(definition =>
        {
            var defaultValue = definition.Read(defaults);
            stored.TryGetValue(definition.Key, out var row);
            var overridden = row != null && definition.Validate(row.Value) == null;
            return new PlatformSettingView(
                definition.Key, definition.Label, definition.Description, definition.Kind, definition.Min, definition.Max,
                defaultValue, overridden ? row!.Value : defaultValue, overridden,
                overridden ? row!.UpdatedAt ?? row.CreatedAt : null, overridden ? row!.UpdatedBy : null);
        }).ToList();
    }

    public async Task SetAsync(string key, string? value, string? actor, CancellationToken token = default)
    {
        var definition = PlatformSettingsCatalog.Find(key?.Trim() ?? string.Empty)
            ?? throw new SubscriptionRuleException($"Unknown setting '{key}'.");
        var error = definition.Validate(value ?? string.Empty);
        if (error != null) throw new SubscriptionRuleException(error);
        var cleaned = value!.Trim();

        var defaults = _configured.Create(Options.DefaultName);
        var row = await Context.PlatformSettings.FirstOrDefaultAsync(s => s.Key == definition.Key, token);
        var before = row?.Value ?? definition.Read(defaults);

        if (cleaned == definition.Read(defaults))
        {
            // Setting it to what configuration already says is "no override" -- keeps resets honest.
            if (row == null) return;
            Context.PlatformSettings.Remove(row);
        }
        else if (row == null)
        {
            await Context.PlatformSettings.AddAsync(new PlatformSetting { Id = Guid.CreateVersion7(), Key = definition.Key, Value = cleaned, UpdatedBy = actor }, token);
        }
        else
        {
            if (row.Value == cleaned) return;
            row.Value = cleaned;
            row.UpdatedBy = actor;
        }

        await _events.AddAsync(null, "settings.changed", new { key = definition.Key, from = before, to = cleaned }, actor, token: token);
    }

    public async Task ResetAsync(string key, string? actor, CancellationToken token = default)
    {
        var definition = PlatformSettingsCatalog.Find(key?.Trim() ?? string.Empty)
            ?? throw new SubscriptionRuleException($"Unknown setting '{key}'.");
        var row = await Context.PlatformSettings.FirstOrDefaultAsync(s => s.Key == definition.Key, token);
        if (row == null) return;

        Context.PlatformSettings.Remove(row);
        await _events.AddAsync(null, "settings.reset", new { key = definition.Key, from = row.Value }, actor, token: token);
    }
}
