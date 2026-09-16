namespace Onepunch.Common.Lib.DTO;

// Published by hrms-api's ApprovalEngineService whenever a configurable approval workflow step
// becomes active (notifying the newly-current approver(s)) or an instance finally resolves
// (notifying the applicant). One message per recipient, mirroring every other notification
// payload in this file. Consumed by NotificationApi's ApprovalNotificationWorker.
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
}
