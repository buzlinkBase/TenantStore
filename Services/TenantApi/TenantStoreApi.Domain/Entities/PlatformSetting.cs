using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;

/// <summary>
/// A platform-wide setting an operator changed from the console, overriding the value in
/// configuration (appsettings / environment). Absent row = "use the configured default".
/// One row per key. Hard-deleted on reset (so the unique key is free again), and every change is
/// also recorded as a BillingEvent with the operator's name.
/// </summary>
[DisableSoftDelete]
public class PlatformSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
