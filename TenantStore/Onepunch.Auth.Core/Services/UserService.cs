using AutoMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using System.Text;

namespace OnePunch.Auth.Core.Services;

public class UserService : BaseService<User>
{
    private readonly UserManager<User> _manager;
    private readonly OutBoxService _outboxService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;
    private readonly EmailTokenService _emailTokenService;
    private readonly ITenantProvider _tenantProvider;
    private readonly Domains _domains;
    private readonly IConfiguration _configuration;
    private readonly KafkaSettings _kafkaOptions;
    private readonly IMapper _mapper;

    public UserService(
        IUnitOfWorkService uow,
        UserManager<User> manager,
        OutBoxService outboxService,
        IPasswordHasher<User> passwordHasher,
        JwtService jwtService,
        IOptions<KafkaSettings> kafkaOptions,
        IOptions<Domains> domains,
        EmailTokenService emailTokenService,
        ITenantProvider tenantProvider,
        IConfiguration configuration,
        IMapper mapper) : base(uow)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _outboxService = outboxService;
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _emailTokenService = emailTokenService;
        _tenantProvider = tenantProvider;
        _domains = domains.Value;
        _configuration = configuration;
        _kafkaOptions = kafkaOptions.Value;
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    #region Registration Helpers
    private RegistrationResult Success(string message) =>
        new RegistrationResult { Success = true, Message = message };
    private RegistrationResult Fail(string code, string message) =>
        new RegistrationResult { Success = false, ErrorCode = code, Message = message };
    //private async Task<RegistrationResult> HandleSuccess(RegistrationResult result, EmailToken token)
    //{
    //    await _publisher.HandleSuccess(result, token);
    //    return result;
    //}
    //private async Task<RegistrationResult> HandleFailure(RegistrationResult result, EmailToken? token)
    //{
    //    await _publisher.HandleFailure(result, token);
    //    return result;
    //}

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

        var emailInfoDb = await FindToken(emailToken);
        if (emailInfoDb == null) throw new Exception("Unverified token");
        if (emailInfoDb.TenantId != userToken.TenantId) return Fail("TENANT_MISMATCH", "Invalid tenant");
        if (emailInfoDb.Email != userToken.Email) return Fail("EMAIL_MISMATCH", "Cannot verify email");
        if (emailInfoDb == null) return Fail("TOKEN_NOT_FOUND", "Unable to verify Token");
        if (IsTokenExpired(emailInfoDb)
            || emailInfoDb.IsUsed) return Fail("TOKEN_EXPIRED", "User registration expired");

        emailInfoDb.IsUsed = true;
        var user = await _manager.FindByEmailAsync(emailInfoDb.Email);
        if (user == null) return Fail("EMAIL_NOT_FOUND", "Email not found");
        if (user.TenantId != emailInfoDb.TenantId) return Fail("TENANT_MISMATCH", "Invalid tenant");
        user.Status = "Active";
        user.EmailConfirmed = true;

        Context.EmailTokens.Update(emailInfoDb);
        Context.Users.Update(user);

        var payload = new TenantUserPayload
        {
            Email = user.Email!,
            TenantId = user.TenantId,
            UserId = user.Id,
        };
        var message = ObjectSerializer.Serialize(new MessagePayload<TenantUserPayload> { Data = payload });
        var confirmationOutbox = _outboxService.CreateModel(
            user.TenantId, user.Id.ToString(),
            _kafkaOptions.Topics.TenantUserConfirmed,
            message);

        await _outboxService.AddAsync(confirmationOutbox, ctoken);
        if (await CommitChangesAsync(ctoken))
        {
            return Success("User successfully activated");
        }
        return Success("User cannot be activated");
    }
    #endregion

    #region User Management

    public async Task<(User user, IdentityResult result)> RegisterInvitesAsync(CreateInvitedUser payload, CancellationToken ctoken)
    {
        var token = Encoding.UTF8.GetString(TokenEncodingHelper.FromBase64Url(payload.Token));
        var userToken = ObjectSerializer.Deserialize<EmailTokenInfo>(token);
        var emailInfoDb = await FindToken(payload.Token);
        if (emailInfoDb == null) throw new Exception("Unverified token");
        if (emailInfoDb.TenantId != userToken.TenantId) throw new Exception("Invalid payload");
        if (emailInfoDb.Email != userToken.Email) throw new Exception("Invalid payload");
        if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed) throw new Exception("Token expired");

        var user = new User
        {
            TenantId = userToken.TenantId,
            UserName = userToken.Email,
            Email = userToken.Email,
            Name = payload.Name ?? userToken.Name,
            Status = "Active",
            EmailConfirmed = true
        };

        emailInfoDb.IsUsed = true;
        Context.EmailTokens.Update(emailInfoDb);
        var result = await _manager.CreateAsync(user, payload.Password);
        if (result.Succeeded)
        {
            await CommitChangesAsync(ctoken);
            return (user, result);
        }
        return (user, result);
    }

    public async Task<bool> SendInvite(InvitationPayload payload, CancellationToken token)
    {
        var exp = DateTime.UtcNow.AddDays(2);
        var emailToken = await _emailTokenService.CreateModelAsync("user.invitation", exp, payload.Email);
        var message = new MessagePayload<UserInvitionNotificationPayload>
        {
            Data = new UserInvitionNotificationPayload
            {
                Email = payload.Email,
                Name = payload.Name,
                InviteLink = $"{_domains.FrontEndDomain}/register?token={emailToken.TokenValue}",
                AppName = _configuration["AppName"],
                TenantName = emailToken.TenantName,
                Expiry = exp,
                Token = emailToken.TokenValue,
            }
        };
        //store token
        await _emailTokenService.StoreToken(emailToken, token);
        //store outbox
        var outbox = _outboxService.CreateModel(
            emailToken.TenantId,
            emailToken.TenantId.ToString(),
            _kafkaOptions.Topics.SendUserInvitation,
            ObjectSerializer.Serialize(message));
        await _outboxService.AddAsync(outbox, token);
        return await CommitChangesAsync(token);
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
            Expiry = DateTime.UtcNow.AddMinutes(15),
            TenantId = user.TenantId,
        };
    }

    public async Task<LoginResponse> RefreshLogin(string refreshToken, CancellationToken token)
    {
        var refreshTokenHash = _jwtService.Hash(refreshToken);
        var tokenEntity = await Context.RefreshTokens
            .FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash);

        if (tokenEntity == null || tokenEntity.Revoked || tokenEntity.Expiry < DateTime.UtcNow)
            return new LoginResponse { Success = false, ErrorMessage = "Invalid or expired refresh token." };

        var user = await Context.Users.FindAsync(tokenEntity.UserId);
        if (user == null)
            return new LoginResponse { Success = false, ErrorMessage = "User not found." };

        tokenEntity.Revoked = true;

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
            Expiry = DateTime.UtcNow.AddMinutes(15)
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
