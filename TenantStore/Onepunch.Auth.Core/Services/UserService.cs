using AutoMapper;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using RTools_NTS.Util;
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
    private readonly EmailTokenService _emailTokenService;
    private readonly TenantService _tenantService;
    private readonly Domains _domains;
    private readonly IConfiguration _configuration;
    private readonly IMapper _mapper;

    public UserService(
        IPublishEndpoint publisher,
        IUnitOfWorkService uow,
        UserManager<User> manager,
        SignInManager<User> signInManager,
        IPasswordHasher<User> passwordHasher,
        JwtService jwtService,
        IOptions<Domains> domains,
        EmailTokenService emailTokenService,
        ITenantProvider tenantProvider,
        TenantService tenantService,
        IConfiguration configuration,
        IMapper mapper) : base(uow)
    {
        _publisher = publisher;
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _signInManager = signInManager;
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _emailTokenService = emailTokenService;
        _tenantService = tenantService;
        _domains = domains.Value;
        _configuration = configuration;
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    #region Registration Helpers
    private RegistrationResult Success(string message) => new RegistrationResult { Success = true, Message = message };
    private RegistrationResult Fail(string code, string message) => new RegistrationResult { Success = false, ErrorCode = code, Message = message };

    public async Task<(User user, IdentityResult result)> RegisterTenantAdmin(TenantCreatedPayload payload)
    {
        var user = new User
        {
            TenantId = payload.TenantId,
            UserName = payload.Email,
            Email = payload.Email,
            Name = "Admin",
            Status = "Pending"
        };
        var result = await _manager.CreateAsync(user, payload.Password);
        return (user, result);
    }
    private async Task<EmailToken?> FindToken(string emailToken) =>
        await Repository.Find<EmailToken>(x => x.TokenValue == emailToken).FirstOrDefaultAsync();
    private bool IsTokenExpired(EmailToken token) => token.Expiry < DateTime.UtcNow;
    #endregion

    #region Registration
    public async Task<RegistrationResult> ConfirmedRegistration(string emailToken, CancellationToken ctoken)
    {
        var token = Encoding.UTF8.GetString(TokenEncodingHelper.FromBase64Url(emailToken));
        var userToken = ObjectSerializer.Deserialize<EmailTokenInfo>(token);

        //update token
        var emailInfoDb = await FindToken(emailToken);
        if (emailInfoDb == null) throw new Exception("Unverified token");
        if (emailInfoDb.TenantId != userToken.TenantId) return Fail("TENANT_MISMATCH", "Invalid tenant");
        if (emailInfoDb.Email != userToken.Email) return Fail("EMAIL_MISMATCH", "Cannot verify email");
        if (emailInfoDb == null) return Fail("TOKEN_NOT_FOUND", "Unable to verify Token");
        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed) return Fail("TOKEN_EXPIRED", "User registration expired");
        emailInfoDb.IsUsed = true;
        Context.EmailTokens.Update(emailInfoDb);

        //update user
        var user = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (user == null) return Fail("EMAIL_NOT_FOUND", "Email not found");
        if (user.TenantId != emailInfoDb.TenantId) return Fail("TENANT_MISMATCH", "Invalid tenant");
        user.Status = "Active";
        user.EmailConfirmed = true;
        Context.Users.Update(user);

        var payload = new TenantUserPayload
        {
            Email = user.Email!,
            TenantId = user.TenantId,
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
        var exp = DateTime.UtcNow.AddDays(1);
        var userInfo = await _manager.FindByEmailAsync(email);
        if (userInfo == null || string.IsNullOrEmpty(email))
        {
            Log.Logger.Error($"user trying to reset password for unknown email {email}");
            return;//user dont exists bypass
        }

        var userToken = await _manager.GeneratePasswordResetTokenAsync(userInfo);
        var tenant = await _tenantService.GetGrpcBgInfoAsync(userInfo.TenantId);
        if (tenant == null)
        {
            Log.Logger.Error($"user trying to reset password for unknown tenant {email}");
            return;
        }
        var emailToken = new CreateEmailToken
        {
            Expiry = exp,
            TokenType = "reset-password",
            TokenValue = userToken,
            TenantId = userInfo.TenantId,
            Email = email,
            TenantName = tenant.Name
        };
        var message = new ResetPasswordEmail
        {
            Email = userInfo.Email ?? email,
            Name = userInfo.Name,
            ResetLink = $"{_domains.FrontEndDomain}/reset-password?token={emailToken.TokenValue}",
            AppName = _configuration["AppName"],
            TenantName = emailToken.TenantName,
            Expiry = exp,
            Token = emailToken.TokenValue,
        };
        await _emailTokenService.StoreToken(emailToken, token);
        await _publisher.Publish(message, token);
        await CommitChangesAsync(token);

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

        return new LoginResponse
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
            TenantId = user.TenantId,
        };

    }

    public async Task<AuthenticationProperties> LoginWithGoogleAsync(string redirectUrl)
    {
        // 2. Configure the properties for the external login
        var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
        return properties;
    }
    public async Task<LoginResponse> GoogleCallback(CancellationToken token)
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return new LoginResponse { Success = false, Message = "External login failed." };
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (email == null)
        {
            return new LoginResponse { Success = false, Message = "Email not provided by Google." };
        }

        // 1. Check if they already have a Google Login linked to an account
        var user = await _manager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user == null)
        {
            // 2. Check if a local email/password account exists (Account Linking)
            user = await _manager.FindByEmailAsync(email); 
            if (user != null)
            {
                // Link Google to the existing local account
                await _manager.AddLoginAsync(user, info);
            }
            else
            {
                // 4. TRULY NEW USER: We need to create the "Account" (Tenant)
                Guid tenantId;
                try
                {
                    // TRY gRPC FIRST: Immediate creation in Tenant Service
                    // Replace with your actual gRPC client call
                    var tenantResponse = await _tenantGrpcClient.CreateAccountAsync(new { Email = email }, cancellationToken: token);
                    tenantId = tenantResponse.Id;
                }
                catch (Exception ex)
                {
                    // FALLBACK TO RMQ: If Tenant Service is down, we don't want to fail the login.
                    // We generate a temp ID or mark it pending, and let the Tenant Service catch up via RMQ.
                    tenantId = Guid.NewGuid(); // Or a specific 'Pending' marker

                    await _publisher.Publish(new CreateAccountMessage
                    {
                        Email = email,
                        TemporaryId = tenantId
                    });
                    // Log the degradation: _logger.LogWarning("Tenant Service unreachable, falling back to RMQ");
                }

                // Create local Auth record with the retrieved or generated TenantId
                user = new User
                {
                    UserName = email,
                    Email = email,
                    TenantId = tenantId,
                    EmailConfirmed = true // Google already verified this email
                };

                var createResult = await _manager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return new LoginResponse { Success = false, Message = "Failed to create user record." };
                }

                await _manager.AddLoginAsync(user, info);
            }
        }

        // Generate Tokens
        var accessToken = await _jwtService.CreateTokenAsync(user);
        var refreshToken = await _jwtService.GenerateRefreshToken();

        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, refreshToken), token);
        await Context.SaveChangesAsync(token);
        await CommitChangesAsync(token);

        return new LoginResponse
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
            TenantId = user.TenantId,
        };
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
        var newAccessToken = await _jwtService.CreateTokenAsync(user);
        var newRefreshToken = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(CreateRefreshToken(user, newRefreshToken));
        await Context.SaveChangesAsync();
        await CommitChangesAsync(token);

        return new LoginResponse
        {
            Success = true,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry)
        };
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
