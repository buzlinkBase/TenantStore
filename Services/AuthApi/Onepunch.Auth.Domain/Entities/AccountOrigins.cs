namespace OnePunch.Auth.Domain.Entities;

/// <summary>How an account began. Stored on <see cref="User.Origin"/> and reported by the platform console's acquisition page.</summary>
public static class AccountOrigins
{
    /// <summary>Signed up with an email and password (and confirmed the email).</summary>
    public const string EmailSignup = "EmailSignup";

    /// <summary>Signed up with a Google account.</summary>
    public const string GoogleSignup = "GoogleSignup";

    /// <summary>The account was made by accepting a staff invitation to someone's company.</summary>
    public const string StaffInvite = "StaffInvite";

    /// <summary>The account was made by accepting a client-portal invitation.</summary>
    public const string ClientInvite = "ClientInvite";

    /// <summary>From before origins were recorded, and not recoverable from what was kept.</summary>
    public const string Unknown = "Unknown";

    public static readonly IReadOnlyList<string> All = [EmailSignup, GoogleSignup, StaffInvite, ClientInvite, Unknown];

    public static bool IsKnown(string? origin) => origin is not null && All.Contains(origin);
}
