using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

public class TenantCreationRequestStatus : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string? TenantName { get; set; }
    public DateTime? RequestExpiry { get; set; }
    public TenantCreationStatus Status { get; set; } = TenantCreationStatus.Initial;

    /// <summary>
    /// Raw status text from the external HRIS system's HrisOrgProvisionedPayload (see
    /// HrDbCreatedWorker) — e.g. "Completed"/"Failed". Null until that message arrives.
    /// Persisted (not just pushed over SignalR) so a fresh page load / reconnect can still
    /// answer "is the HR database ready?" via TenantRequestController.GetStatus.
    /// </summary>
    public string? HrDbStatus { get; set; }
    public bool HrDbReady { get; set; }
}

public enum TenantCreationStatus
{
    Initial = 0,               // Request received, entry created in the host database
    AwaitingApproval = 1,      // (Optional) Waiting for an admin to click "Approve"
    Provisioning = 2,          // Background worker is running EF migrations/creating DBs
    Created = 3,               // Infrastructure is complete, system ready
    Onboarding = 4,            // Infrastructure ready, but user hasn't completed onboarding wizard
    Active = 5,                // Fully active and operating normally
    Failed = 6,                // Something went wrong during automated infrastructure build
    Rejected = 7,               // Admin explicitly denied the registration request
    Deactivated = 8,
    Suspended = 9,
    Expired = 10,
}