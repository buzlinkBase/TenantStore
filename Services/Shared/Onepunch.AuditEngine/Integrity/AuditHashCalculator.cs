using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Onepunch.AuditEngine.Integrity;

/// <summary>
/// The tamper-evidence hash: SHA-256 over a canonical serialization of an event's content plus
/// its predecessor's hash. Canonical means fixed field order, sorted metadata keys and a fixed
/// timestamp format, so the same event always hashes the same regardless of which store it was
/// read back from.
/// </summary>
public static class AuditHashCalculator
{
    public static string Compute(AuditEvent e)
    {
        var canonical = new object?[]
        {
            e.Id.ToString("D"),
            FormatTimestamp(e.OccurredAtUtc),
            e.EventType,
            e.EntityType,
            e.EntityId,
            e.OrganizationId,
            e.Actor?.Id,
            e.Actor?.DisplayName,
            e.Actor?.Type,
            e.Description,
            e.Changes.Select(c => new[] { c.Property, c.OldValue, c.NewValue }).ToArray(),
            e.Metadata.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => new[] { m.Key, m.Value }).ToArray(),
            e.Outcome.ToString(),
            e.Source,
            e.PreviousEventHash,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // Microsecond precision (what MySQL datetime(6) keeps) and no Kind suffix, so a value read
    // back from storage -- truncated, Kind=Unspecified -- still hashes identically.
    internal static string FormatTimestamp(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture);

    /// <summary>Truncates to microseconds and marks the value UTC.</summary>
    public static DateTime NormalizeTimestamp(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
