using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using Serilog;
using System.Security.Claims;

namespace OnePunch.Auth.Core.Services;

public class UserService : BaseService<User>
{
    private readonly IPublishEndpoint _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<User> _manager;
    private readonly RoleManager<Role> _roleManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;
    private readonly EmailNotificationService _notificationService;
    private readonly IMapper _mapper;
    public UserService(
        IUnitOfWorkService uow,
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

        IMapper mapper) : base(uow)
    {
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _roleManager = roleManager;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _notificationService = notificationService;
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }
    #region Registration Helpers
    private RegistrationResult Success(string message) => new RegistrationResult { Success = true, Message = message };
    private RegistrationResult Fail(string code, string message) => new RegistrationResult { Success = false, ErrorCode = code, Message = message };
    private async Task<EmailToken?> FindToken(string emailToken) =>
        await Repository.Find<EmailToken>(x => x.TokenValue == emailToken).FirstOrDefaultAsync();
    private bool IsTokenExpired(EmailToken token) => token.Expiry < DateTime.UtcNow;
    #endregion

    #region Registration
    public async Task RegisterAccount(CreateAccount payload, CancellationToken token)
    {
        var user = await _manager.FindByEmailAsync(payload.Email);
        if (user == null)
        {
            user = new User
            {
                UserName = payload.Email,
                Email = payload.Email,
                FullName = payload.Name ?? "Admin",
                Status = "Pending"
            };
            var result = await _manager.CreateAsync(user, payload.Password);
            await _manager.AddToRoleAsync(user, "Admin");
            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Unable to create an account";
                throw new Exception(error);
            }
        }
        else
        {
            if (!user.EmailConfirmed)
            {
                await _notificationService.SendEmailVerification(user, token);
            }
            throw new Exception("An account with this email already exists.");
        }
        if (!user.EmailConfirmed)
        {
            await _notificationService.SendEmailVerification(user, token);
        }
        await _manager.UpdateAsync(user);
        await CommitChangesAsync(token);
    }

    public async Task<RegistrationResult> ConfirmedRegistration(string emailToken, CancellationToken ctoken)
    {
        var emailInfoDb = await FindToken(emailToken);
        if (emailInfoDb == null) throw new Exception("Invalid token");
        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed) return Fail("TOKEN_EXPIRED", "User registration expired");
        var user = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (user == null) return Fail("USER_NOT_FOUND", "user not found");

        user.Status = "Active";
        user.EmailConfirmed = true;
        Repository.Update(user);
        await RemoveAsync(emailInfoDb.Id);
        await CommitChangesAsync(ctoken);
        return Success("User successfully activated");
    }
    #endregion
    #region User Management 

    public async Task<IdentityResult?> ChangePassword(ChangePassword payload, CancellationToken token)
    {
        if (payload.NewPassword != payload.ConfirmPassword)
        {
            throw new Exception("Password dont match");
        }
        var user = await _manager.FindByEmailAsync(payload.Email);
        if (user == null) throw new Exception("unable to change password");
        return await _manager.ChangePasswordAsync(user, payload.OldPassword, payload.NewPassword);
    }
    public async Task<IdentityResult> PromoteToPasswordAccount(string? userId, string newPassword, CancellationToken token)
    {
        var user = await _manager.FindByIdAsync(userId ?? "");
        if (user == null) throw new Exception("User not found");
        var hasPassword = await _manager.HasPasswordAsync(user);
        if (hasPassword)
        {
            throw new Exception("User already has a password. Use ChangePassword instead.");
        }
        var result = await _manager.AddPasswordAsync(user, newPassword);
        if (result.Succeeded)
        {
            await CommitChangesAsync();
        }
        return result;
    }
    public async Task ResetPasswordRequestAsync(string email, CancellationToken token)
    {
        var userInfo = await _manager.FindByEmailAsync(email);
        if (userInfo == null || string.IsNullOrEmpty(email))
        {
            Log.Logger.Error($"user trying to reset password for unknown email {email}");
            return;
        }
        var userToken = await _manager.GeneratePasswordResetTokenAsync(userInfo);
        await _notificationService.SendResetPassword(userInfo, userToken, token);
    }

    public async Task<IdentityResult> ResetPassword(ResetPassword payload, CancellationToken ctoken)
    {
        if (payload.Password != payload.ConfirmPassword) throw new Exception("Password dont match");
        var emailInfoDb = await FindToken(payload.Token);
        if (emailInfoDb == null) throw new Exception("Unverified token");
        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed)
        {
            await RemoveAsync(emailInfoDb.Id);
            await CommitChangesAsync(ctoken);
            throw new Exception("Token expired");
        }
        var userInfo = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (userInfo == null) throw new Exception("User not found");
        if (userInfo == null)
        {
            // Don't reveal the user doesn't exist; just return a generic error
            return IdentityResult.Failed(new IdentityError { Description = "Invalid Request" });
        }
        var result = await _manager.ResetPasswordAsync(userInfo, payload.Token, payload.Password);
        await RemoveAsync(emailInfoDb.Id);
        await CommitChangesAsync(ctoken);
        return result;
    }

    public Task<User?> GetByIdAsync(string id) => _manager.FindByIdAsync(id);
    public Task<User?> GetByEmailAsync(string email) => _manager.FindByEmailAsync(email);
    public async Task<IdentityResult> UpdateAsync(UpdateUser payload)
    {
        var user = await _manager.FindByIdAsync(payload.Id.ToString());
        if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found" });
        _mapper.Map(payload, user);
        return await _manager.UpdateAsync(user);
    }
    public async Task<IdentityResult> UpdateAsync(User user) =>
        user == null
            ? IdentityResult.Failed(new IdentityError { Description = "User not found" })
            : await _manager.UpdateAsync(user);
    public async Task<IdentityResult> DeleteAsync(string id)
    {
        var user = await _manager.FindByIdAsync(id);
        return user == null
            ? IdentityResult.Failed(new IdentityError { Description = "User not found" })
            : await _manager.DeleteAsync(user);
    }
    #endregion
    #region Authentication
    public async Task<LoginResponse> Login(LoginPayload payload, CancellationToken token)
    {
        var user = await GetByEmailAsync(payload.Email);
        if (user == null ||
            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? "", payload.Password) == PasswordVerificationResult.Failed)
        {
            return new LoginResponse { ErrorMessage = "Invalid email or password." };
        }
        if (user.Status != "Active" || !user.EmailConfirmed)
        {
            return new LoginResponse { ErrorMessage = "User is not found" };
        }
        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshToken), token);
        await Context.SaveChangesAsync(token);
        await CommitChangesAsync(token);
        return ComposeLoginRespose(user, accessToken, refreshToken);
    }

    internal LoginResponse ComposeLoginRespose(User user, string accessToken, string refreshToken)
    {
        //TODO capture users tenants
        var tenants = new List<UsersTenant>();
        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
            Tenants = tenants
        };
    }
    public async Task<AuthenticationProperties> LoginWithGoogleAsync(string redirectUrl)
    {
        // 2. Configure the properties for the external login
        var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
        return properties;
    }

    public async Task CreateTenant(User user, string CompanyName = "", CancellationToken token = default)
    {
        var newTenant = new TenantCreationRequest
        {
            UserId = user.Id,
            TenantName = CompanyName ?? "My Organization",
        };
        await _publisher.Publish(newTenant, token);
    }

    public async Task<LoginResponse> SetDefaultTenant(Guid tenantId, string token, CancellationToken ct)
    {
        var tokenInfo = _jwtService.ReadTokenToObject(token);
        if (tokenInfo == null || !string.IsNullOrWhiteSpace(tokenInfo.ErrorMessage)) throw new UnauthorizedException();
        var user = await _manager.FindByIdAsync(tokenInfo.UserId.ToString());
        if (user == null) throw new UnauthorizedException();
        user.DefaultTenantId = tenantId;
        await _manager.UpdateAsync(user);

        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshTokenString = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), ct);
        await CommitChangesAsync(ct);
        return ComposeLoginRespose(user, accessToken, refreshTokenString);

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
            // 1. Find or Create User
            var user = await _manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user == null)
            {
                user = await _manager.FindByEmailAsync(email);
                if (user != null)
                {
                    // Link Google to existing email account
                    var linkResult = await _manager.AddLoginAsync(user, info);
                    if (!linkResult.Succeeded)
                        return new LoginResponse { ErrorMessage = "Failed to link Google account." };
                }
                else
                {
                    // Create brand new user
                    user = new User
                    {
                        UserName = email,
                        Email = email,
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
            await _manager.UpdateAsync(user);
            var accessToken = await _jwtService.CreateTokenAsync(user);
            var refreshTokenString = await _jwtService.GenerateRefreshToken();
            await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshTokenString), token);
            await CommitChangesAsync(token);
            return ComposeLoginRespose(user, accessToken, refreshTokenString);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "google callback failed, email: {email}", email);
            return new LoginResponse { ErrorMessage = "An internal error occurred during setup." };
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
        await RemoveAsync(tokenEntity.Id);

        var accessToken = await _jwtService.CreateTokenAsync(user);
        var newRefreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, newRefreshToken));
        await Context.SaveChangesAsync();
        await CommitChangesAsync(token);
        return ComposeLoginRespose(user, accessToken, newRefreshToken);
    }
    #endregion
    #region Helpers
    internal RefreshToken CreateRefreshToken(User user, string newRefreshToken) =>
        new RefreshToken
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
