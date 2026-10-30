using BuzlinkRepository;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnePunch.Auth.Domain.Entities;

public class User : IdentityUser<Guid>, IEntity
{
    public string? FullName { get; set; }
    public Guid? DefaultTenantId { get; set; }
    public string? DefaultTenantName { get; set; }

    /// <summary>
    /// Denormalized cache of the user's role(s) in DefaultTenantId — Tenant Service is the
    /// source of truth (UserMembership.Roles); this is only for cheap reads when minting a
    /// token without an extra gRPC round-trip. Stored as a delimited string via EF conversion
    /// (see AuthContext.OnModelCreating) rather than a separate table, since it's just a cache.
    /// </summary>
    public List<string> DefaultTenantRoles { get; set; } = new();
    public string Status { get; set; } = "Active";

    /// <summary>
    /// How the account began: created by a client invitation (a client's own staff) rather than by signing up. It is only a record of that;
    /// it limits nothing. The same login works in the HRIS (sign-in, Google, password reset), where a person who belongs to no company is
    /// asked to set one up and gets a trial like any new user, since a client's staff may become customers.
    /// </summary>
    public bool PortalOnly { get; set; }

    /// <summary>
    /// Where the account came from (<see cref="AccountOrigins"/>), recorded when it is created. "Unknown" is an account from before this was
    /// recorded that the backfill could not place; it is shown as such, never guessed.
    /// </summary>
    public string Origin { get; set; } = AccountOrigins.Unknown;

    /// <summary>What the origin points at: the invitation id for an invited account (a staff or client invitation).</summary>
    public string? OriginDetail { get; set; }

    /// <summary>When the account was created. Null for an account whose date could not be recovered.</summary>
    public DateTime? CreatedAtUtc { get; set; }

    public DateTime? EmailConfirmedAtUtc { get; set; }

    /// <summary>The first time the person signed in to the HRIS app (not the client portal).</summary>
    public DateTime? FirstHrisSignInAtUtc { get; set; }

    /// <summary>The first time the person signed in to the client portal.</summary>
    public DateTime? FirstPortalSignInAtUtc { get; set; }

    /// <summary>The Terms of Service version the person agreed to when signing up (the box on the sign-up page).
    /// A company's own acceptance is kept in Tenant Service (TermsAcceptance); this is the person's.</summary>
    public int? TermsVersionAccepted { get; set; }
    public DateTime? TermsAcceptedAtUtc { get; set; }
    public string? TermsAcceptedIp { get; set; }

    /// <summary>Records the person agreeing to the Terms at sign-up. A missing version (an older client) records nothing.</summary>
    public User AgreedToTerms(int? version, DateTime now, string? ip)
    {
        if (version is not > 0) return this;
        TermsVersionAccepted = version;
        TermsAcceptedAtUtc = now;
        TermsAcceptedIp = ip is { Length: > 64 } ? ip[..64] : ip;
        return this;
    }

    // "First" times are set once and never moved, so these only fill an empty value.
    public void StampEmailConfirmed(DateTime now) => EmailConfirmedAtUtc ??= now;
    public void StampFirstHrisSignIn(DateTime now) => FirstHrisSignInAtUtc ??= now;
    public void StampFirstPortalSignIn(DateTime now) => FirstPortalSignInAtUtc ??= now;

    /// <summary>Records how a new account began. Call it on the new <see cref="User"/> before it is saved.</summary>
    public User BeganAs(string origin, DateTime now, string? detail = null)
    {
        Origin = origin;
        OriginDetail = detail;
        CreatedAtUtc = now;
        return this;
    }

    [NotMapped]
    public string EntityType { get; set; } = string.Empty;
}
public class Role : IdentityRole<Guid>, IEntity { }