using Google.Apis.Auth;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using OnePunch.Auth.Core.Auditing;
using Onepunch.Auth.Core.Interfaces;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using Serilog;
using System.Security.Claims;
using System.Text.Json;

namespace OnePunch.Auth.Core.Services;

public class UserService : BaseService<User>
{
    private readonly MembershipCacheService _membershipCacheService;
    private readonly MembershipGrpcClient _membershipGrpcClient;
    private readonly IPublishEndpoint _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<User> _manager;
    private readonly RoleManager<Role> _roleManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;
    private readonly IConfiguration _configuration;
    private readonly EmailNotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly IMfaChallengeService _mfaChallengeService;
    private readonly AuthAuditRecorder _auditRecorder;

    public UserService(
        IUnitOfWorkService uow,
        MembershipCacheService membershipCacheService,
        MembershipGrpcClient membershipGrpcClient,
        IPublishEndpoint publisher,
        IHttpContextAccessor httpContextAccessor,
        UserManager<User> manager,
        RoleManager<Role> roleManager,
        SignInManager<User> signInManager,
        IPasswordHasher<User> passwordHasher,
        JwtService jwtService,
        ITenantProvider tenantProvider,
        TenantService tenantService,
        IConfiguration configuration,
        EmailTokenService emailTokenService,
        InvitationService invitationService,
        EmailNotificationService notificationService,
        IMfaChallengeService mfaChallengeService,
        IMapper mapper,
        AuthAuditRecorder auditRecorder) : base(uow)
    {
        _auditRecorder = auditRecorder;
        _membershipCacheService = membershipCacheService;
        _membershipGrpcClient = membershipGrpcClient;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _roleManager = roleManager;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _configuration = configuration;
        _notificationService = notificationService;
        _mfaChallengeService = mfaChallengeService;
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    #region Registration Helpers
    private RegistrationResult Success(string message, string? email = null, string? name = "") =>
        new() { Success = true, Message = message, Email = email, Name = name };
    private RegistrationResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    private async Task<EmailToken?> FindToken(string emailToken) =>
        await Repository.Find<EmailToken>(x => x.TokenValue == emailToken).FirstOrDefaultAsync();
    private bool IsTokenExpired(EmailToken token) => token.Expiry < DateTime.UtcNow;
    #endregion

    #region Registration
    public async Task<RegistrationResult> RegisterAccount(CreateAccount payload, string accountApiHost, CancellationToken token)
    {
        var existing = await _manager.FindByEmailAsync(payload.Email);
        if (existing != null)
        {
            if (!existing.EmailConfirmed)
            {
                await _notificationService.SendEmailVerification(existing, accountApiHost, token);
            }
            return Success("Please check your email to verify your account.", existing.Email);
        }

        var user = new User
        {
            UserName = payload.Email,
            Email = payload.Email,
            FullName = payload.Name,
            Status = "Pending"
        }.BeganAs(AccountOrigins.EmailSignup, DateTime.UtcNow);

        var createResult = await _manager.CreateAsync(user, payload.Password);
        if (!createResult.Succeeded)
        {
            var error = createResult.Errors.FirstOrDefault()?.Description ?? "Unable to create account.";
            return Fail("CREATE_FAILED", error);
        }

        await _notificationService.SendEmailVerification(user, accountApiHost, token);
        return Success("Account created. Check your email to verify.", user.Email, user.FullName);
    }

    public async Task<RegistrationResult> ConfirmedRegistration(string emailToken, CancellationToken ctoken)
    {
        var emailInfoDb = await FindToken(emailToken);
        if (emailInfoDb == null) return Fail("INVALID_TOKEN", "Invalid verification link.");
        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed)
            return Fail("TOKEN_EXPIRED", "Verification link has expired.");

        var user = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (user == null) return Fail("USER_NOT_FOUND", "Account not found.");

        user.Status = "Active";
        user.EmailConfirmed = true;
        user.StampEmailConfirmed(DateTime.UtcNow);

        var updateResult = await _manager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return Fail("UPDATE_FAILED", "Failed to activate account.");

        emailInfoDb.IsUsed = true;
        Context.EmailTokens.Update(emailInfoDb);
        await CommitChangesAsync(ctoken);
        return Success("Account verified successfully.", user.Email, user.FullName);
    }

    /// <summary>
    /// Applies MembershipChangedWorker's per-message side effect: syncing the DefaultTenantRoles
    /// fallback cache for a role change (see ResolveDefaultRoles). A status change is
    /// deliberately NOT applied here -- MembershipChanged.NewStatus is a per-tenant
    /// UserMembership.Status value (TenantApi), and this AuthApi User row has no legitimate
    /// per-tenant status field to mirror it into. An earlier version of this method wrote it
    /// straight into the account-wide User.Status, which Login/LoginWithGoogleAsync2/RefreshLogin
    /// all gate on regardless of tenant -- so revoking one tenant membership silently locked the
    /// account out of every tenant it belonged to. Tenant-scoped status is already tracked live
    /// via gRPC (MembershipCacheService/MembershipGrpcClient) and forced-logout for it is already
    /// handled independently by MembershipChangedWorker's NotifySessionRevoked push.
    /// </summary>
    public async Task ApplyMembershipChangeAsync(Guid userId, Guid tenantId, string? newStatus, bool isRoleChange, List<string>? newRoles, CancellationToken token)
    {
        var hasStatusChange = !string.IsNullOrWhiteSpace(newStatus);
        var hasRoleChange = isRoleChange && newRoles is { Count: > 0 };
        if (!hasStatusChange && !hasRoleChange) return;

        var user = await _manager.FindByIdAsync(userId.ToString());
        if (user == null) return;

        var changed = false;

        // A revoked default tenant can no longer be used as the user's fallback tenant. Only
        // clear it when this event belongs to the current default tenant; another tenant's
        // revocation must not disturb the user's valid default selection.
        if (string.Equals(newStatus, "Revoked", StringComparison.OrdinalIgnoreCase) &&
            user.DefaultTenantId == tenantId)
        {
            user.DefaultTenantId = null;
            user.DefaultTenantName = null;
            user.DefaultTenantRoles = new();
            changed = true;
        }

        // Only relevant for the user's own DefaultTenantId -- a role edit in some other tenant
        // the user also belongs to doesn't touch this fallback cache.
        if (hasRoleChange && user.DefaultTenantId == tenantId)
        {
            user.DefaultTenantRoles = ResolveDefaultRoles(user.DefaultTenantRoles, newRoles!);
            changed = true;
        }

        if (!changed) return;
        await _manager.UpdateAsync(user);
        await CommitChangesAsync(token);
    }

    /// <summary>
    /// A company was renamed (TenantRenamed, from Tenant Service): brings Auth Service's own
    /// copies of the name in line -- User.DefaultTenantName (the source of every access token's
    /// tenantName claim, so refreshed/new tokens carry the new name), still-pending invitations
    /// (the name shown on the invite email/accept page), and the tenant creation request row.
    /// Idempotent: re-applying the same name is a no-op write, safe under MassTransit's retries.
    /// </summary>
    public async Task ApplyTenantRenameAsync(Guid tenantId, string tenantName, CancellationToken token)
    {
        var users = await Context.Users.Where(x => x.DefaultTenantId == tenantId).ToListAsync(token);
        foreach (var user in users)
            user.DefaultTenantName = tenantName;

        var invitations = await Context.Invitations
            .Where(x => x.TenantId == tenantId && x.Status == InvitationStatus.Pending)
            .ToListAsync(token);
        foreach (var invitation in invitations)
            invitation.TenantName = tenantName;

        var requests = await Context.TenantCreationRequests
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(token);
        foreach (var request in requests)
            request.TenantName = tenantName;

        await CommitChangesAsync(token);
    }

    /// <summary>Pure rule behind <see cref="ChangedRoles"/>, split out so it's testable without
    /// standing up UserManager/Identity plumbing.</summary>
    public static List<string> ResolveDefaultRoles(List<string> currentDefaults, List<string> newRoles)
    {
        var stillValid = currentDefaults.Where(newRoles.Contains).ToList();
        return stillValid.Count > 0 ? stillValid : new List<string> { newRoles[0] };
    }

    #endregion

    #region User Management
    /// <summary>
    /// Changes the signed-in user's own password. The account comes from the caller's token, never
    /// the request body. UserManager saves inside the unit of work's open transaction, so this
    /// must commit or the new password is rolled back when the request ends, while the caller is
    /// still told it worked.
    ///
    /// A changed password signs the account out everywhere else: every other device's refresh
    /// token is revoked, so none of them can renew its session. The device that made the change
    /// (<paramref name="currentRefreshToken"/>) stays signed in.
    /// </summary>
    public async Task<IdentityResult?> ChangePassword(
        string? userId, ChangePassword payload, CancellationToken token, string? currentRefreshToken = null)
    {
        if (payload.NewPassword != payload.ConfirmPassword)
            throw new GuardException("Passwords do not match.");
        var user = await _manager.FindByIdAsync(userId ?? "");
        if (user == null) throw new GuardException("Invalid request.");
        var result = await _manager.ChangePasswordAsync(user, payload.OldPassword, payload.NewPassword);
        if (result.Succeeded)
        {
            await RevokeSessionsAsync(user.Id, currentRefreshToken, token);
            await CommitChangesAsync(token);
        }
        return result;
    }

    /// <summary>
    /// Revokes every live session (refresh token) of the account except <paramref name="keepRefreshToken"/>.
    /// Saved with the caller's commit, so it never happens without the password change it follows.
    /// </summary>
    private async Task RevokeSessionsAsync(Guid userId, string? keepRefreshToken, CancellationToken token)
    {
        var keepHash = string.IsNullOrWhiteSpace(keepRefreshToken) ? null : _jwtService.Hash(keepRefreshToken);
        var sessions = await Context.RefreshTokens
            .Where(t => t.UserId == userId && !t.Revoked)
            .ToListAsync(token);
        foreach (var session in sessions.Where(s => s.RefreshTokenHash != keepHash))
            session.Revoked = true;
    }

    public async Task<IdentityResult> PromoteToPasswordAccount(string? userId, string newPassword, CancellationToken token)
    {
        var user = await _manager.FindByIdAsync(userId ?? "");
        if (user == null) throw new GuardException("User not found.");
        if (await _manager.HasPasswordAsync(user))
            throw new GuardException("User already has a password. Use change-password instead.");
        var result = await _manager.AddPasswordAsync(user, newPassword);
        if (result.Succeeded)
            await CommitChangesAsync(token);
        return result;
    }

    public async Task ResetPasswordRequestAsync(string email, string frontEndHost, CancellationToken token)
    {
        if (string.IsNullOrEmpty(email)) return;
        var userInfo = await _manager.FindByEmailAsync(email);
        if (userInfo == null)
        {
            Log.Logger.Warning("Password reset requested for unknown email {Email}", email);
            return;
        }
        var hasPassword = await _manager.HasPasswordAsync(userInfo);
        if (hasPassword)
        {
            await SendForgotPasswordAsync(userInfo, frontEndHost, token);
        }
        else
        {
            await SendGoogleSignInAsync(userInfo, frontEndHost, token);
        }
    }

    private async Task SendForgotPasswordAsync(User userInfo, string frontEndHost, CancellationToken token)
    {
        var userToken = await _manager.GeneratePasswordResetTokenAsync(userInfo);
        string routeSafeToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(userToken));
        await _notificationService.SendResetPassword(userInfo, frontEndHost, routeSafeToken, token);
    }
    private async Task SendGoogleSignInAsync(User userInfo, string frontEndHost, CancellationToken token)
    {
        await _notificationService.SendGoogleSiginInform(userInfo, frontEndHost, token);
    }

    /// <summary>
    /// Sets a new password from a reset link. Whoever reset it isn't signed in, so every session
    /// of the account is signed out -- including one that may belong to whoever made a reset
    /// necessary. <see cref="PasswordResetResult.UserId"/> is set once it succeeded.
    /// </summary>
    public async Task<PasswordResetResult> ResetPassword(ResetPassword payload, CancellationToken ctoken)
    {
        var emailInfoDb = await FindToken(payload.Token);
        if (emailInfoDb == null) throw new GuardException("Invalid or expired token.");

        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed)
        {
            emailInfoDb.IsUsed = true;
            Context.EmailTokens.Update(emailInfoDb);
            await CommitChangesAsync(ctoken);
            throw new GuardException("Reset link has expired. Please request a new one.");
        }

        var userInfo = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (userInfo == null) throw new GuardException("Invalid request.");

        var decodedTokenBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(payload.Token);
        var actualToken = System.Text.Encoding.UTF8.GetString(decodedTokenBytes);

        var result = await _manager.ResetPasswordAsync(userInfo, actualToken, payload.NewPassword);
        emailInfoDb.IsUsed = true;
        Context.EmailTokens.Update(emailInfoDb);
        if (result.Succeeded)
            await RevokeSessionsAsync(userInfo.Id, keepRefreshToken: null, ctoken);
        await CommitChangesAsync(ctoken);
        return new PasswordResetResult(result, result.Succeeded ? userInfo.Id : null);
    }

    public Task<User?> GetByIdAsync(string id) => _manager.FindByIdAsync(id);
    public Task<User?> GetByEmailAsync(string email) => _manager.FindByEmailAsync(email);

    // Commits for the same reason as ChangePassword: UserManager's own save is rolled back otherwise.
    public async Task<IdentityResult> UpdateProfileAsync(Guid userId, UpdateProfileRequest payload, CancellationToken token = default)
    {
        var user = await _manager.FindByIdAsync(userId.ToString());
        if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
        if (payload.FullName != null) user.FullName = payload.FullName;
        if (payload.PhoneNumber != null) user.PhoneNumber = payload.PhoneNumber;
        var result = await _manager.UpdateAsync(user);
        if (result.Succeeded)
            await CommitChangesAsync(token);
        return result;
    }

    public async Task<IdentityResult> UpdateAsync(UpdateUser payload)
    {
        var user = await _manager.FindByIdAsync(payload.Id.ToString());
        if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
        _mapper.Map(payload, user);
        return await _manager.UpdateAsync(user);
    }

    public async Task<IdentityResult> UpdateAsync(User user) =>
        user == null ? IdentityResult.Failed(new IdentityError { Description = "User not found." }) : await _manager.UpdateAsync(user);

    public async Task<IdentityResult> DeleteAsync(string id)
    {
        var user = await _manager.FindByIdAsync(id);
        return user == null
            ? IdentityResult.Failed(new IdentityError { Description = "User not found." })
            : await _manager.DeleteAsync(user);
    }
    #endregion

    #region Authentication
    public async Task<LoginResponse> Login(LoginPayload payload, CancellationToken token)
    {
        const string method = AuthAuditRecorder.PasswordMethod;
        var user = await GetByEmailAsync(payload.Email);
        // An account created through a client invitation signs in here like any other: the person may become an HRIS customer.
        if (user == null)
            return await SignInFailedAsync(payload.Email, null, "Unknown account", "Invalid email or password.", method, token);

        if (await _manager.IsLockedOutAsync(user))
            return await SignInFailedAsync(payload.Email, user, "Account locked",
                "Account is temporarily locked. Please try again later.", method, token);

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? "", payload.Password) == PasswordVerificationResult.Failed)
        {
            await _manager.AccessFailedAsync(user);
            if (await _manager.IsLockedOutAsync(user))
                return await SignInFailedAsync(payload.Email, user, "Wrong password; account locked after too many attempts",
                    "Too many failed attempts. Account is temporarily locked.", method, token);
            return await SignInFailedAsync(payload.Email, user, "Wrong password", "Invalid email or password.", method, token);
        }

        if (user.Status != "Active" || !user.EmailConfirmed)
            return await SignInFailedAsync(payload.Email, user, "Account not active",
                "Account is not active. Please verify your email.", method, token);
        // Flow C(login) MFA extension point: no-op today (see NoOpMfaChallengeService), so this
        // never actually short-circuits login yet, but the seam is in place for a real MFA
        // implementation to plug into later.
        if (await _mfaChallengeService.IsChallengeRequiredAsync(user))
        {
            return new LoginResponse
            {
                MfaRequired = true,
                MfaChallengeToken = _jwtService.GenerateKey(16)
            };
        }

        await _manager.ResetAccessFailedCountAsync(user);
        user.StampFirstHrisSignIn(DateTime.UtcNow);
        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshToken), token);
        await _auditRecorder.SignedInAsync(user, method, token);
        await CommitChangesAsync(token);
        return await ComposeLoginResponse(user, accessToken, refreshToken);
    }

    // A failed sign-in has no business change to ride along with, so it commits on its own --
    // which also persists Identity's failed-attempt counter (AccessFailedAsync), so lockout after
    // repeated failures takes effect.
    private async Task<LoginResponse> SignInFailedAsync(
        string attemptedEmail, User? user, string auditReason, string errorMessage, string method, CancellationToken token)
    {
        await _auditRecorder.SignInFailedAsync(attemptedEmail, user, auditReason, method, token);
        await CommitChangesAsync(token);
        return new LoginResponse { ErrorMessage = errorMessage };
    }
    /// <param name="sessionTenantId">
    /// The company the access token is scoped to, when it isn't the account's default (a
    /// refreshed session that's working in another company) -- Roles/Permissions must describe
    /// the same company as the token.
    /// </param>
    internal async Task<LoginResponse> ComposeLoginResponse(User user, string accessToken, string refreshToken, Guid? sessionTenantId = null)
    {
        var tenants = await _membershipCacheService.GetMembershipsAsync(user.Id);
        // Roles and permissions must come from the same fresh membership snapshot. Using the
        // denormalized DefaultTenantRoles cache here can leave only one stale role after a role
        // update; when that role is Employee, the frontend incorrectly treats the user as
        // employee-only and hides every non-portal menu. The membership snapshot is the source
        // of truth and preserves the complete role set regardless of role order.
        var tokenTenantId = sessionTenantId ?? user.DefaultTenantId;
        var tokenTenant = tenants.FirstOrDefault(t => t.TenantId == tokenTenantId);
        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
            Tenants = tenants,
            Name = user.FullName,
            Roles = tokenTenant?.Roles ?? [],
            Permissions = tokenTenant?.Permissions ?? [],
            Email = user.Email,
        };
    }

    /// <summary>
    /// for full api google 
    /// </summary>
    /// <param name="redirectUrl"></param>
    /// <returns></returns>
    public async Task<AuthenticationProperties> LoginWithGoogleAsync(string redirectUrl)
    {
        return _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
    }

    /// <summary>
    /// Exchanges a Google auth-code (popup/auth-code flow) for the validated id_token payload.
    /// Returns null if Google didn't return a token; throws InvalidJwtException if the token is invalid.
    /// </summary>
    private async Task<GoogleJsonWebSignature.Payload?> ExchangeGoogleCodeAsync(string code, CancellationToken ct)
    {
        // Explicit timeout (HttpClient's own default is ~100s) plus the caller's own
        // CancellationToken threaded through -- previously neither existed, so a slow/hung
        // Google token endpoint could hold this request open for up to 100s with no way for
        // the caller (or a request abort) to cut it short.
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _configuration["Authentication:Google:ClientId"]!,
                ["client_secret"] = _configuration["Authentication:Google:ClientSecret"]!,
                ["redirect_uri"] = "postmessage", // required for popup/auth-code flow
                ["grant_type"] = "authorization_code",
            }), ct
        );

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);

        if (!tokenData.TryGetProperty("id_token", out var idTokenElement))
            return null;

        return await GoogleJsonWebSignature.ValidateAsync(
            idTokenElement.GetString(),
            new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _configuration["Authentication:Google:ClientId"] }
            }
        );
    }

    /// <summary>
    /// Sign in with Google — existing accounts only. Does not create a new account.
    /// </summary>
    public async Task<LoginResponse> LoginWithGoogleAsync2(string code, CancellationToken ct)
    {
        try
        {
            var payload = await ExchangeGoogleCodeAsync(code, ct);
            if (payload == null)
                return new LoginResponse { ErrorMessage = "Failed to retrieve token from Google." };

            const string method = AuthAuditRecorder.GoogleMethod;
            var user = await _manager.FindByEmailAsync(payload.Email);
            if (user == null)
                return await SignInFailedAsync(payload.Email, null, "Unknown account",
                    "No account found for this Google email. Please sign up first.", method, ct);

            // Same account-validity gate as password Login/RefreshLogin — Google is an
            // authentication method, not a bypass for lockout/deactivation.
            if (await _manager.IsLockedOutAsync(user))
                return await SignInFailedAsync(payload.Email, user, "Account locked",
                    "Account is temporarily locked. Please try again later.", method, ct);

            if (user.Status != "Active" || !user.EmailConfirmed)
                return await SignInFailedAsync(payload.Email, user, "Account not active", "Account is not active.", method, ct);

            user.StampFirstHrisSignIn(DateTime.UtcNow);
            var refreshTokenString = await _jwtService.GenerateRefreshToken();
            var accessToken = await _jwtService.CreateTokenAsync(user);
            await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), ct);
            await _auditRecorder.SignedInAsync(user, method, ct);
            await CommitChangesAsync(ct);
            return await ComposeLoginResponse(user, accessToken, refreshTokenString);
        }
        catch (InvalidJwtException)
        {
            return new LoginResponse { ErrorMessage = "Invalid Google token." };
        }
        catch (Exception ex)
        {
            return new LoginResponse { ErrorMessage = $"Google login failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Sign up with Google — creates a new account. Fails if an account already exists for the email.
    /// </summary>
    public async Task<LoginResponse> SignUpWithGoogleAsync(string code, CancellationToken ct)
    {
        try
        {
            var payload = await ExchangeGoogleCodeAsync(code, ct);
            if (payload == null)
                return new LoginResponse { ErrorMessage = "Failed to retrieve token from Google." };

            var existing = await _manager.FindByEmailAsync(payload.Email);
            if (existing != null)
                return new LoginResponse { ErrorMessage = "An account with this Google email already exists. Please sign in instead." };

            var user = new User
            {
                Email = payload.Email,
                UserName = payload.Email,
                FullName = payload.Name,
                EmailConfirmed = true, // Google already verified the email
                Status = "Active",
            }.BeganAs(AccountOrigins.GoogleSignup, DateTime.UtcNow);
            user.StampEmailConfirmed(user.CreatedAtUtc!.Value);
            user.StampFirstHrisSignIn(user.CreatedAtUtc.Value);

            var createResult = await _manager.CreateAsync(user);
            if (!createResult.Succeeded)
                return new LoginResponse
                {
                    ErrorMessage = string.Join(", ", createResult.Errors.Select(e => e.Description))
                };

            var refreshTokenString = await _jwtService.GenerateRefreshToken();
            var accessToken = await _jwtService.CreateTokenAsync(user);
            await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), ct);
            await _auditRecorder.SignedInAsync(user, $"{AuthAuditRecorder.GoogleMethod} sign-up", ct);
            await CommitChangesAsync(ct);
            return await ComposeLoginResponse(user, accessToken, refreshTokenString);
        }
        catch (InvalidJwtException)
        {
            return new LoginResponse { ErrorMessage = "Invalid Google token." };
        }
        catch (Exception ex)
        {
            return new LoginResponse { ErrorMessage = $"Google sign-up failed: {ex.Message}" };
        }
    }

    public async Task CreateTenant(User user, string CompanyName = "", CancellationToken token = default)
    {
        var newTenant = new TenantCreationRequested
        {
            UserId = user.Id,
            TenantName = CompanyName ?? "My Organization",
        };
        await _publisher.Publish(newTenant, token);
    }

    public async Task<LoginResponse> SetDefaultTenant(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var user = await _manager.FindByIdAsync(userId.ToString());
        if (user == null) throw new UnauthorizedException();

        // Flow F: verify the caller actually has a membership in the target tenant before
        // minting a token scoped to it, rather than trusting the client-supplied tenantId.
        var membership = await _membershipGrpcClient.ResolveMembershipAsync(user.Id, tenantId, deadlineMilliseconds: 2000);
        if (!membership.Success || !membership.Found || membership.Status != "Active")
            throw new ForbiddenException("You are not an active member of this tenant.");

        user.DefaultTenantId = tenantId;
        user.DefaultTenantName = membership.TenantName;
        user.DefaultTenantRoles = membership.Roles;
        await _manager.UpdateAsync(user);

        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshTokenString = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString, tenantId, membership.TenantName), ct);
        await CommitChangesAsync(ct);
        return await ComposeLoginResponse(user, accessToken, refreshTokenString);
    }

    public async Task<LoginResponse> GoogleCallback(CancellationToken token)
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
            return new LoginResponse { ErrorMessage = "External login failed." };

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
            return new LoginResponse { ErrorMessage = "Email not provided by Google." };

        try
        {
            var user = await _manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user == null)
            {
                user = await _manager.FindByEmailAsync(email);
                if (user != null)
                {
                    var linkResult = await _manager.AddLoginAsync(user, info);
                    if (!linkResult.Succeeded)
                        return new LoginResponse { ErrorMessage = "Failed to link Google account." };
                }
                else
                {
                    user = new User
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        FullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0],
                        Status = "Active"
                    }.BeganAs(AccountOrigins.GoogleSignup, DateTime.UtcNow);
                    var createResult = await _manager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        var error = createResult.Errors.FirstOrDefault()?.Description ?? "User creation failed.";
                        return new LoginResponse { ErrorMessage = error };
                    }
                    await _manager.AddLoginAsync(user, info);
                }
            }

            var accessToken = await _jwtService.CreateTokenAsync(user);
            var refreshTokenString = await _jwtService.GenerateRefreshToken();
            await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), token);
            await CommitChangesAsync(token);
            return await ComposeLoginResponse(user, accessToken, refreshTokenString);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Google callback failed for {Email}", email);
            return new LoginResponse { ErrorMessage = "An internal error occurred during sign-in." };
        }
    }

    public async Task<User?> Profile(ClaimsPrincipal user)
    {
        return await _manager.GetUserAsync(user);
    }

    public async Task<LoginResponse> RefreshLogin(string refreshToken, CancellationToken token)
    {
        var refreshTokenHash = _jwtService.Hash(refreshToken);
        var tokenEntity = await Context.RefreshTokens
            .FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash);

        // A Client Portal session's token is not valid here (see RefreshToken.Audience).
        if (tokenEntity == null || tokenEntity.Revoked || tokenEntity.Expiry <= DateTime.UtcNow || tokenEntity.Audience != null)
            return new LoginResponse { ErrorMessage = "Invalid or expired refresh token." };

        var user = await Context.Users.FindAsync(tokenEntity.UserId);
        if (user == null)
            return new LoginResponse { ErrorMessage = "User not found." };

        // Mirror Login's account-validity checks — a revoked/locked/deactivated account
        // must not be able to mint fresh tokens just because it's still holding a live
        // refresh token from before the lockout/deactivation happened.
        if (await _manager.IsLockedOutAsync(user))
        {
            tokenEntity.Revoked = true;
            await CommitChangesAsync(token);
            return new LoginResponse { ErrorMessage = "Account is temporarily locked. Please try again later." };
        }

        if (user.Status != "Active" || !user.EmailConfirmed)
        {
            tokenEntity.Revoked = true;
            await CommitChangesAsync(token);
            return new LoginResponse { ErrorMessage = "Account is not active." };
        }

        tokenEntity.Revoked = true;
        // A refresh is the client's own periodic sync point -- invalidate the membership cache
        // first so both the new access token's role claims and this response's Roles/Permissions
        // reflect the caller's absolute latest membership, not whatever was cached (up to 5 min
        // old) the last time it was resolved.
        //await _membershipCacheService.InvalidateAsync(user.Id);

        // Stay in the company this session was working in -- not the account-wide default,
        // which a switch on another device may have moved since.
        var memberships = await _membershipCacheService.GetMembershipsAsync(user.Id);
        var creationPending = tokenEntity.TenantId is { } requested &&
            await Context.TenantCreationRequests.AnyAsync(x =>
                x.TenantId == requested && x.UserId == user.Id && x.Status != TenantCreationStatus.Created, token);
        var session = SessionTenantResolver.Resolve(
            tokenEntity.TenantId, tokenEntity.TenantName, user.DefaultTenantId, user.DefaultTenantName,
            memberships, creationPending);

        var accessToken = session.TenantId is not { } sessionTenantId
            ? await _jwtService.CreateTokenAsync(user)
            // A company the user is still creating has no membership row to read roles from --
            // they're its Owner, same as when WorkspaceService minted their first token for it.
            : await _jwtService.CreateTokenAsync(user, sessionTenantId.ToString(), session.TenantName,
                overrideRoles: session.CreationPending ? ["Owner"] : null);
        var newRefreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, newRefreshToken, session.TenantId, session.TenantName));
        await CommitChangesAsync(token);

        var response = await ComposeLoginResponse(user, accessToken, newRefreshToken, session.TenantId);
        if (session.CreationPending)
        {
            response.Roles = ["Owner"];
            response.Permissions = [];
        }
        return response;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var hash = _jwtService.Hash(refreshToken);
        var tokenEntity = await Context.RefreshTokens
            .FirstOrDefaultAsync(t => t.RefreshTokenHash == hash && !t.Revoked, ct);
        if (tokenEntity == null) return;
        tokenEntity.Revoked = true;
        var user = await _manager.FindByIdAsync(tokenEntity.UserId.ToString());
        if (user != null)
            await _auditRecorder.SignedOutAsync(user, ct);
        await CommitChangesAsync(ct);
    }
    #endregion

    #region Helpers
    /// <summary>
    /// A refresh token for a session working in <paramref name="tenantId"/> -- the default
    /// company when none is given. The company is fixed at issue time, so a later change of the
    /// account-wide default (a switch on another device) doesn't move this session.
    /// </summary>
    internal RefreshToken CreateRefreshToken(User user, string newRefreshToken, Guid? tenantId = null, string? tenantName = null) =>
        new()
        {
            UserId = user.Id,
            RefreshTokenHash = _jwtService.Hash(newRefreshToken),
            Expiry = DateTime.UtcNow.AddDays(_jwtService.RefreshExpiry),
            CreatedAt = DateTime.UtcNow,
            Revoked = false,
            TenantId = tenantId ?? user.DefaultTenantId,
            TenantName = tenantId != null ? tenantName : user.DefaultTenantName,
        };
    public string KeyGen() => _jwtService.GenerateKey(32);
    #endregion
}

/// <summary>A password reset's outcome, and whose account it was once it succeeded.</summary>
public sealed record PasswordResetResult(IdentityResult Result, Guid? UserId);
