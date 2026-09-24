namespace Onepunch.Common.Lib.DTO;

// Published by hrms-api's ApprovalEngineService whenever a configurable approval workflow step
// becomes active (notifying the newly-current approver(s)) or an instance finally resolves
// (notifying the applicant). One message per recipient, mirroring every other notification
// payload in this file. Consumed by NotificationApi's ApprovalNotificationWorker (email, gated
// by DeliverEmail) and by hrms-api's own ApprovalPushNotificationWorker (SignalR push, gated by
// DeliverPush) -- both consumers just respect the flags already resolved once at publish time
// (see ApprovalEngineService.ResolveNotificationRecipientsAsync), so neither needs its own
// notification-preference lookup.
public record ApprovalNotificationRequested
{
    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string ApplicationTypeLabel { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    // "Pending Your Approval" | "Approved" | "Declined"
    public string StatusLabel { get; set; } = string.Empty;
    public int? StepNumber { get; set; }
    public int? TotalSteps { get; set; }
    public string? Note { get; set; }

    // Added for the SignalR push channel (hrms-api's ApprovalPushNotificationWorker). Kept as
    // plain string/Guid rather than hrms-api's own ApprovalApplicationType enum, since this DTO
    // is compiled into tenantstore too (NotificationApi has no reference to hrms-api's domain
    // project) -- ApplicationType carries the enum's .ToString() (e.g. "Leave", "Dtr"), the same
    // string ASP.NET's route-model-binding already uses for GET /approvals/{applicationType}/...,
    // so it round-trips through hrms-api's own Enum.Parse<ApprovalApplicationType> unchanged.
    public string ApplicationType { get; set; } = string.Empty;
    public Guid ApplicationId { get; set; }
    public Guid ApprovalInstanceId { get; set; }

    // Recipient's linked portal-user id (Employee.UserId), null when the recipient has no portal
    // login -- push is simply skipped for those (email is unaffected). Resolved once per
    // recipient in ApprovalEngineService.ResolveNotificationRecipientsAsync, same place
    // DeliverEmail/DeliverPush are resolved.
    public Guid? RecipientUserId { get; set; }

    // Per-recipient, per-category channel gate from NotificationPreference (hrms-api's tenant
    // DB) -- both true when no preference row exists (opt-out model). Resolved once here so
    // neither downstream consumer needs its own preference lookup or cross-service call.
    public bool DeliverEmail { get; set; } = true;
    public bool DeliverPush { get; set; } = true;
}
