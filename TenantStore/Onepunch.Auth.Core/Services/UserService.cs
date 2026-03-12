using AutoMapper;
using Azure.Core;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using Serilog;
using System.Security.Claims;
using System.Text;

namespace OnePunch.Auth.Core.Services;

public class UserService : BaseService<User>
{
    private readonly IPublishEndpoint _publisher;
    private readonly UserManager<User> _manager;
    private readonly SignInManager<User> _signInManager;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;
    private readonly TenantService _tenantService;
    private readonly Domains _domains;
    private readonly IConfiguration _configuration;
    private readonly EmailNotificationService _notificationService;
    private readonly IMapper _mapper;
    public UserService(
        IPublishEndpoint publisher,
        IUnitOfWorkService uow,
        UserManager<User> manager,
        SignInManager<User> signInManager,
        IPasswordHasher<User> passwordHasher,
        JwtService jwtService,
        IOptions<Domains> domains,
        ITenantProvider tenantProvider,
        TenantService tenantService,
        IConfiguration configuration,
        EmailNotificationService notificationService,
        IMapper mapper) : base(uow)
    {
        _publisher = publisher;
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _signInManager = signInManager;
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _tenantService = tenantService;
        _domains = domains.Value;
        _configuration = configuration;
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
    public async Task<(User user, IdentityResult result)> RegisterAccount(CreateAccount payload, CancellationToken token)
    {
        var user = await _manager.FindByEmailAsync(payload.Email);
        if (user != null)
        {
            if (!string.IsNullOrEmpty(payload.InviteToken))
            {
                var invitation = await Context.Invitations
                     .FirstOrDefaultAsync(i => i.Token == payload.InviteToken && i.Expiry > DateTime.UtcNow, token);
                if (invitation != null)
                {
                    await HandleJoin(user, invitation);
                }
                else
                {
                    throw new Exception("Token expired");
                }
            }
            else
            {
                throw new Exception("Email already exists, login instead");
            }
        }
        var newAccount = new User
        {
            UserName = payload.Email,
            Email = payload.Email,
            FullName = payload.Name ?? "Admin",
            Status = "Pending"
        };
        var result = await _manager.CreateAsync(newAccount, payload.Password);
        if (!result.Succeeded)
        {
            throw new Exception("Unable to create an account");
        }
        await CreateTenant(newAccount, payload.CompanyName, token);
        await _notificationService.SendEmailVerification(newAccount, token);
        return (newAccount, result);
    }

    private async Task HandleJoin(User user,Invitation invitation)
    {
        Context.Invitations.Remove(invitation);
        await Task.CompletedTask;
    }
    public async Task<RegistrationResult> ConfirmedRegistration(string emailToken, CancellationToken ctoken)
    {
        var token = Encoding.UTF8.GetString(TokenEncodingHelper.FromBase64Url(emailToken));
        var userToken = ObjectSerializer.Deserialize<EmailTokenInfo>(token);
        if (userToken == null) throw new Exception("Unable to verify token");
        //update token
        var emailInfoDb = await FindToken(emailToken);
        if (emailInfoDb == null) throw new Exception("Unverified token");
        if (emailInfoDb.Email != userToken.Email) return Fail("EMAIL_MISMATCH", "Cannot verify email");
        if (emailInfoDb == null) return Fail("TOKEN_NOT_FOUND", "Unable to verify Token");
        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed) return Fail("TOKEN_EXPIRED", "User registration expired");
        emailInfoDb.IsUsed = true;
        Context.EmailTokens.Update(emailInfoDb);

        //update user
        var user = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (user == null) return Fail("EMAIL_NOT_FOUND", "Email not found");
        user.Status = "Active";
        user.EmailConfirmed = true;
        Context.Users.Update(user);

        var payload = new TenantUserPayload
        {
            Email = user.Email!,
            UserId = user.Id,
        };

        await _publisher.Publish(payload, ctoken);

        if (await CommitChangesAsync(ctoken))
        {
            return Success("User successfully activated");
        }
        return Success("User cannot be activated");
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
            return new LoginResponse { Success = false, ErrorMessage = "Invalid email or password." };
        }
        if (user.Status != "Active" || !user.EmailConfirmed)
        {
            return new LoginResponse { Success = false, ErrorMessage = "User is not found" };
        }

        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshToken), token);
        await Context.SaveChangesAsync(token);
        await CommitChangesAsync(token);
        return ComposeLoginRespose(user, accessToken, refreshToken);
    }

    private LoginResponse ComposeLoginRespose(User user, string accessToken, string refreshToken)
    {
        //TODO capture users tenants
        var tenants = new List<UsersTenant>();
        return new LoginResponse
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
            DefaultTenantId = user.DefaultTenantId,
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
        var newTenant = new UserCreated
        {
            UserId = user.Id,
            CompanyName = CompanyName ?? "My Organization",
        };
        await _publisher.Publish(newTenant, token);
    }

    public async Task SetDefaultTenant(Guid tenantId, string token, CancellationToken ct)
    {

        var tokenInfo = _jwtService.ReadTokenToObject(token);
        if (tokenInfo == null || !string.IsNullOrWhiteSpace(tokenInfo.ErrorMessage)) return;
        var user = await _manager.FindByIdAsync(tokenInfo.UserId.ToString());
        if (user == null) return;
        user.DefaultTenantId = tenantId;
        await _manager.UpdateAsync(user);
        await CommitChangesAsync(ct);


    }


    public async Task<LoginResponse> GoogleCallback(string? inviteToken, CancellationToken token)
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null) return new LoginResponse { Success = false, ErrorMessage = "External login failed." };
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (email == null) return new LoginResponse { Success = false, ErrorMessage = "Email not provided by Google." };
        try
        {
            var user = await _manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user == null)
            {
                user = await _manager.FindByEmailAsync(email);
                if (user != null)
                {
                    await _manager.AddLoginAsync(user, info);
                }
                else
                {
                    user = new User
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        FullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0]
                    };
                    var createResult = await _manager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        return new LoginResponse { Success = false, ErrorMessage = "Failed to create user record." };
                    }
                    await _manager.AddLoginAsync(user, info);
                }
            }

            if (!string.IsNullOrEmpty(inviteToken))
            {
                // FLOW: User was invited. Link them to the existing tenant.
                var invitation = await Context.Invitations
                    .FirstOrDefaultAsync(i => i.Token == inviteToken && i.Expiry > DateTime.UtcNow, token);
                if (invitation != null)
                {
                    await HandleJoin(user, invitation);
                }
                else
                {
                    throw new Exception("Token expired");
                }
            }
            else
            {
                //create tenant if not invited
                var createTenant = new UserCreated
                {
                    UserId = user.Id,
                    CompanyName = email.Split('@')[0],
                };
                await _publisher.Publish(createTenant, token);
            }
            await _manager.UpdateAsync(user);
            await Context.SaveChangesAsync(token);
            var accessToken = await _jwtService.CreateTokenAsync(user);
            var refreshToken = await _jwtService.GenerateRefreshToken();
            await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshToken), token);
            await Context.SaveChangesAsync(token);
            return ComposeLoginRespose(user, accessToken, refreshToken);
        }
        catch (Exception ex)
        {
            return new LoginResponse { Success = false, ErrorMessage = "An error occurred during account setup." };
        }
    }

    public async Task<LoginResponse> RefreshLogin(string refreshToken, CancellationToken token)
    {
        var refreshTokenHash = _jwtService.Hash(refreshToken);
        var tokenEntity = await Context.RefreshTokens
            .FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash);

        if (tokenEntity == null || tokenEntity.Revoked || tokenEntity.Expiry <= DateTime.UtcNow)
            return new LoginResponse { Success = false, ErrorMessage = "Invalid or expired refresh token." };

        var user = await Context.Users.FindAsync(tokenEntity.UserId);
        if (user == null)
            return new LoginResponse { Success = false, ErrorMessage = "User not found." };

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
    private RefreshToken CreateRefreshToken(User user, string newRefreshToken) =>
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
