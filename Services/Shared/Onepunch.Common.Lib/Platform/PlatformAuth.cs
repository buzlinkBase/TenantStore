namespace Onepunch.Common.Lib.Platform;

/// <summary>
/// Wire-level constants of the Platform Admin Console's identity. The console's operators are NOT
/// tenant users: they live in their own store (AuthApi's PlatformAuthContext) and get tokens with
/// their own issuer, audience and signing key, so a tenant token can never pass for a platform
/// one (or the other way round). Shared by AuthApi (mints) and TenantApi (validates).
/// </summary>
public static class PlatformAuthDefaults
{
    /// <summary>Authentication scheme name for platform JWTs on the services that validate them.</summary>
    public const string Scheme = "Platform";

    public const string Issuer = "Onepunch.Platform";

    /// <summary>Audience of platform ACCESS tokens -- what the platform API endpoints accept.</summary>
    public const string Audience = "Onepunch.Platform";

    /// <summary>
    /// Audience of the short-lived token handed out after a correct password but before the MFA code.
    /// Different from <see cref="Audience"/> so it can't be used to call anything.
    /// </summary>
    public const string ChallengeAudience = "Onepunch.Platform.Challenge";

    public const string JwksPath = "/.well-known/platform-jwks.json";

    public const string PermissionClaim = "permission";
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    /// <summary>Authentication-method claim; platform access tokens are only ever minted after a password AND an authenticator code.</summary>
    public const string MethodClaim = "amr";
    public const string OtpMethod = "otp";

    /// <summary>Policy: an authenticated operator whose token proves the authenticator step (not just the password).</summary>
    public const string MfaPolicy = "platform.mfa";
}

public static class PlatformRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Billing = "Billing";
    public const string Support = "Support";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Billing, Support];

    public static bool IsKnown(string? role) => role != null && All.Contains(role);
}

/// <summary>Granular platform permissions; policies and the console's UI gate on these, never on role names.</summary>
public static class PlatformPermissions
{
    public const string AccountsRead = "platform.accounts.read";
    public const string SubscriptionsManage = "platform.subscriptions.manage";
    public const string PaymentsRecord = "platform.payments.record";
    /// <summary>Sending money back to a payer through the payment gateway -- apart from recording or voiding one.</summary>
    public const string PaymentsRefund = "platform.payments.refund";
    public const string PlansManage = "platform.plans.manage";
    public const string SettingsManage = "platform.settings.manage";
    public const string AuditRead = "platform.audit.read";
    public const string AdminsManage = "platform.admins.manage";
    /// <summary>Requesting and approving the permanent erasure of an account (two different operators needed).</summary>
    public const string AccountsDelete = "platform.accounts.delete";
    /// <summary>Deleting a company's stored files (attachments, photos, logos) to free an account's storage.</summary>
    public const string StorageFilesDelete = "platform.storage.files.delete";
    /// <summary>Setting up a new company for an account owner, on their behalf (the owner can't add one for themselves unless they own the company they are in).</summary>
    public const string CompaniesCreate = "platform.companies.create";

    public static readonly IReadOnlyList<string> All =
    [
        AccountsRead, SubscriptionsManage, PaymentsRecord, PaymentsRefund, PlansManage, SettingsManage, AuditRead, AdminsManage,
        AccountsDelete, StorageFilesDelete, CompaniesCreate,
    ];
}

/// <summary>
/// Which permissions each platform role holds. Data, not branching: a new role or permission is one
/// entry here, and nothing that checks permissions changes.
/// </summary>
public static class PlatformRolePermissions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Map =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [PlatformRoles.SuperAdmin] = PlatformPermissions.All,
            [PlatformRoles.Billing] =
            [
                PlatformPermissions.AccountsRead,
                PlatformPermissions.SubscriptionsManage,
                PlatformPermissions.PaymentsRecord,
                PlatformPermissions.PaymentsRefund,
                PlatformPermissions.PlansManage,
            ],
            [PlatformRoles.Support] =
            [
                PlatformPermissions.AccountsRead,
                PlatformPermissions.AuditRead,
                PlatformPermissions.CompaniesCreate,
            ],
        };

    /// <summary>The permissions of <paramref name="role"/>; empty for an unknown role (fails closed).</summary>
    public static IReadOnlyList<string> For(string? role) =>
        role != null && Map.TryGetValue(role, out var permissions) ? permissions : [];
}
