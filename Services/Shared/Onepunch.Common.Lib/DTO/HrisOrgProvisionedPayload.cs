namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Flow C/J contract: published by the external HRIS system once it has finished creating a
/// tenant's HR organization + database, after observing this platform's TenantCreationCompleted
/// signal (realized on the wire as <see cref="TenantCreationCompleted"/> — see
/// HrDbCreatedWorker/TenantCreatedWorker). Consumed here only to trigger the "tenant-added"
/// SignalR push (TenantNotificationService.NotifyHrDbCreated) back to the waiting client.
/// Distinct from the generic <see cref="ConnectionStringPayload"/> (which any ServiceOwner's
/// database-provisioning flow can publish) so this platform never has to string-match on
/// ServiceOwner=="hrms" to tell HRIS-specific events apart from other modules'.
/// </summary>
public record HrisOrgProvisionedPayload
{
    public Guid TenantId { get; set; }
    public string HrisOrgId { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ProvisionedAtUtc { get; set; } = DateTime.UtcNow;
}
