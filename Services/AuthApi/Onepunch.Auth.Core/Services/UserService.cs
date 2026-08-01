using Google.Apis.Auth;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
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
        IMapper mapper) : base(uow)
    {
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
        };

        var createResult = await _manager.CreateAsync(user, payload.Password);
        if (!createResult.Succeeded)
        {
            var error = createResult.Errors.FirstOrDefault()?.Description ?? "Unable to create account.";
            Log.Logger.Error("User creation failed: {Error}", error);
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

        var updateResult = await _manager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return Fail("UPDATE_FAILED", "Failed to activate account.");

        emailInfoDb.IsUsed = true;
        Context.EmailTokens.Update(emailInfoDb);
        await CommitChangesAsync(ctoken);
        return Success("Account verified successfully.", user.Email, user.FullName);
    }
    #endregion

    #region User Management
    public async Task<IdentityResult?> ChangePassword(ChangePassword payload, CancellationToken token)
    {
        if (payload.NewPassword != payload.ConfirmPassword)
            throw new GuardException("Passwords do not match.");
        var user = await _manager.FindByEmailAsync(payload.Email);
        if (user == null) throw new GuardException("Invalid request.");
        return await _manager.ChangePasswordAsync(user, payload.OldPassword, payload.NewPassword);
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

    public async Task<IdentityResult> ResetPassword(ResetPassword payload, CancellationToken ctoken)
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
        await CommitChangesAsync(ctoken);
        return result;
    }

    public Task<User?> GetByIdAsync(string id) => _manager.FindByIdAsync(id);
    public Task<User?> GetByEmailAsync(string email) => _manager.FindByEmailAsync(email);

    public async Task<IdentityResult> UpdateProfileAsync(Guid userId, UpdateProfileRequest payload)
    {
        var user = await _manager.FindByIdAsync(userId.ToString());
        if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found." });
        if (payload.FullName != null) user.FullName = payload.FullName;
        if (payload.PhoneNumber != null) user.PhoneNumber = payload.PhoneNumber;
        return await _manager.UpdateAsync(user);
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
        var user = await GetByEmailAsync(payload.Email);
        if (user == null)
            return new LoginResponse { ErrorMessage = "Invalid email or password." };

        if (await _manager.IsLockedOutAsync(user))
            return new LoginResponse { ErrorMessage = "Account is temporarily locked. Please try again later." };

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? "", payload.Password) == PasswordVerificationResult.Failed)
        {
            await _manager.AccessFailedAsync(user);
            if (await _manager.IsLockedOutAsync(user))
                return new LoginResponse { ErrorMessage = "Too many failed attempts. Account is temporarily locked." };
            return new LoginResponse { ErrorMessage = "Invalid email or password." };
        }

        if (user.Status != "Active" || !user.EmailConfirmed)
            return new LoginResponse { ErrorMessage = "Account is not active. Please verify your email." };

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
        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshToken), token);
        await CommitChangesAsync(token);
        return await ComposeLoginResponse(user, accessToken, refreshToken);
    }
    internal async Task<LoginResponse> ComposeLoginResponse(User user, string accessToken, string refreshToken)
    {
        var tenants = await _membershipCacheService.GetMembershipsAsync(user.Id);
        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
            Tenants = tenants,
            Name = user.FullName,
            Roles = user.DefaultTenantRoles,
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

    public async Task<LoginResponse> LoginWithGoogleAsync2(string code, CancellationToken ct)
    {
        try
        {
            // Exchange auth code for Google tokens
            var tokenResponse = await new HttpClient().PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = _configuration["Authentication:Google:ClientId"]!,
                    ["client_secret"] = _configuration["Authentication:Google:ClientSecret"]!,
                    ["redirect_uri"] = "postmessage", // required for popup/auth-code flow
                    ["grant_type"] = "authorization_code",
                })
            );

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);

            if (!tokenData.TryGetProperty("id_token", out var idTokenElement))
                return new LoginResponse { ErrorMessage = "Failed to retrieve token from Google." };

            // Validate the id_token and extract user info
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idTokenElement.GetString(),
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _configuration["Authentication:Google:ClientId"] }
                }
            );

            // Find or create user
            var user = await _manager.FindByEmailAsync(payload.Email);
            if (user == null)
            {
                user = new User
                {
                    Email = payload.Email,
                    UserName = payload.Email,
                    FullName = payload.Name,
                    EmailConfirmed = true, // Google already verified the email,
                    Status = "Active",
                };

                var createResult = await _manager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return new LoginResponse
                    {
                        ErrorMessage = string.Join(", ", createResult.Errors.Select(e => e.Description))
                    };
            }
            // Generate your JWT same as normal login
            var refreshTokenString = await _jwtService.GenerateRefreshToken();
            var accessToken = await _jwtService.CreateTokenAsync(user);
            await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), ct);
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

    public async Task CreateTenant(User user, string CompanyName = "", CancellationToken token = default)
    {
        var newTenant = new TenantCreationRequested
        {
            UserId = user.Id,
            TenantName = CompanyName ?? "My Organization",
        };
        await _publisher.Publish(newTenant, token);
    }

    public async Task<LoginResponse> SetDefaultTenant(Guid tenantId, Guid userId , CancellationToken ct)
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
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), ct);
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
                    };
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

        if (tokenEntity == null || tokenEntity.Revoked || tokenEntity.Expiry <= DateTime.UtcNow)
            return new LoginResponse { ErrorMessage = "Invalid or expired refresh token." };

        var user = await Context.Users.FindAsync(tokenEntity.UserId);
        if (user == null)
            return new LoginResponse { ErrorMessage = "User not found." };

        tokenEntity.Revoked = true;

        var accessToken = await _jwtService.CreateTokenAsync(user);
        var newRefreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, newRefreshToken));
        await CommitChangesAsync(token);
        return await ComposeLoginResponse(user, accessToken, newRefreshToken);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var hash = _jwtService.Hash(refreshToken);
        var tokenEntity = await Context.RefreshTokens
            .FirstOrDefaultAsync(t => t.RefreshTokenHash == hash && !t.Revoked, ct);
        if (tokenEntity == null) return;
        tokenEntity.Revoked = true;
        await CommitChangesAsync(ct);
    }
    #endregion

    #region Helpers
    internal RefreshToken CreateRefreshToken(User user, string newRefreshToken) =>
        new()
        {
            UserId = user.Id,
            RefreshTokenHash = _jwtService.Hash(newRefreshToken),
            Expiry = DateTime.UtcNow.AddDays(_jwtService.RefreshExpiry),
            CreatedAt = DateTime.UtcNow,
            Revoked = false
        };
    public string KeyGen() => _jwtService.GenerateKey(32);
    #endregion
}
