using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

public class EmailNotificationService
{

    private readonly EmailTokenService _emailTokenService;
    private readonly IConfiguration _configuration;
    private readonly Domains _domainOptions;
    private readonly IPublishEndpoint _publisher;

    public EmailNotificationService(
        EmailTokenService emailTokenService,
        IConfiguration configuration,
        IOptions<Domains> domainOptions,
        IPublishEndpoint publisher)
    {
        _emailTokenService = emailTokenService;
        _configuration = configuration;
        _domainOptions = domainOptions.Value;
        _publisher = publisher;
    }
    public async Task SendEmailVerification(User account, CancellationToken token)
    {
        if (account == null || account?.Email == null) return;
        var exp = DateTime.UtcNow.AddDays(1);

        var tokenModel = await _emailTokenService.CreateModelAsync("user.created", exp, account.Id, account.Email);
        await _emailTokenService.StoreToken(tokenModel, token); 
        var emailDomain= new UserEmailPayload
        {
            Token = tokenModel.TokenValue,
            Email = account.Email!,
            AppName = _configuration["AppName"] ?? "OnePunch",
            FullName = account.Name ?? "User",
            Expiry = tokenModel?.Expiry ?? DateTime.UtcNow.AddDays(2),
            IssuedAt = DateTime.UtcNow,
            Purpose = "account confirmation",
            ConfirmationRoute = $"{_domainOptions.AuthDomain}/api/v1/users/confirm-email?token={token}"
        };
        await _publisher.Publish(emailDomain, token);
        await _emailTokenService.CommitChangesAsync(token);
    }

    public async Task SendResetPassword(User account, string userToken, CancellationToken token)
    {
        if (account == null || account?.Email == null) return;
        var exp = DateTime.UtcNow.AddHours(4);
        var message = new ResetPasswordEmail
        {
            Email = account.Email,
            Name = account.Name ?? "User",
            ResetLink = $"{_domainOptions.FrontEndDomain}/reset-password?token={userToken}",
            AppName = _configuration["AppName"] ?? "",
            Expiry = exp,
            Token = userToken,
        };
        var tokenModel = await _emailTokenService.CreateModelAsync("user.created", exp, account.Id, account.Email, userToken);
        await _emailTokenService.StoreToken(tokenModel, token);
        await _publisher.Publish(message, token);
        await _emailTokenService.CommitChangesAsync(token);
    } 
}

public class EmailTokenService : BaseService<EmailToken>
{
    private readonly TenantService _tenantService;
    public EmailTokenService(IUnitOfWorkService uow,
        TenantService tenantService) : base(uow)
    {
        _tenantService = tenantService;
    }
    public async Task<CreateEmailToken> CreateModelAsync(
        string TokenType,
        DateTime expiry,
        Guid UserId,
        string email,
        string? emailToken = null)
    {
        var token = emailToken ?? TokenGenerator.Generate(email, UserId);
        return new CreateEmailToken
        {
            Expiry = expiry,
            TokenType = TokenType,
            TokenValue = token,
            Email = email,
        };
    }
    public async Task StoreToken(CreateEmailToken payload, CancellationToken token)
    {
        var model = new EmailToken
        {
            Email = payload.Email,
            TokenValue = payload.TokenValue,
            TokenType = payload.TokenType,
            Expiry = payload.Expiry,
            Status = "Active",
            UserId = payload.UserId,
        };
        await Repository.AddAsync(model, token);
    }
}

public class CreateEmailToken
{
    public Guid UserId { get; set; }
    public DateTime Expiry { get; set; }
    public string TokenType { get; set; }
    public string TokenValue { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
}