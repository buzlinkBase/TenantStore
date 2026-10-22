namespace TenantStoreApi.Core;

/// <summary>
/// The system is for the Philippines only, so scheduled jobs run on Philippine time (Asia/Manila),
/// like hrms-api's PhTime. Stored instants stay UTC; this is only where a wall-clock schedule is read.
/// </summary>
public static class PhilippineTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
}
