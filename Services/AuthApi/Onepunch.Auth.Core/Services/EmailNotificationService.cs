using MassTransit;
using Microsoft.AspNetCore.Http;
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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Domains _domainOptions;
    private readonly IPublishEndpoint _publisher;

    public EmailNotificationService(
        EmailTokenService emailTokenService,
        IConfiguration configuration,
        IOptions<Domains> domainOptions,
        IHttpContextAccessor httpContextAccessor,
        IPublishEndpoint publisher)
    {
        _emailTokenService = emailTokenService;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _domainOptions = domainOptions.Value;
        _publisher = publisher;
    }

    public async Task SendEmailVerification(User account, CancellationToken ct)
    {
        if (account?.Email == null) return;

        var baseUrl = _domainOptions.BaseUrl;
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
            baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        var exp = DateTime.UtcNow.AddDays(1);
        var tokenModel = await _emailTokenService.CreateModel(exp, account.Email);
        await _emailTokenService.StoreToken(tokenModel, ct);

        await _publisher.Publish(new SendAccountVerification
        {
            Email = account.Email,
            FullName = account.FullName ?? "User",
            ConfirmationRoute = $"{baseUrl}/api/v1/users/confirm-email?token={tokenModel.TokenValue}"
        }, ct);
        await _emailTokenService.CommitChangesAsync(ct);
    }

    public async Task SendResetPassword(User account, string userToken, CancellationToken token)
    {
        if (account == null || account?.Email == null) return;
        var exp = DateTime.UtcNow.AddHours(4);
        var message = new ResetPasswordEmail
        {
            Email = account.Email,
            Name = account.FullName ?? "User",
            ResetLink = $"{_domainOptions.FrontEnd}/reset-password?token={userToken}",
            AppName = _configuration["AppName"] ?? "",
            Expiry = exp,
            Token = userToken,
        };
        var tokenModel = await _emailTokenService.CreateModel("user.reset.password", exp, account.Id, account.Email, userToken);
        await _emailTokenService.StoreToken(tokenModel, token);
        await _publisher.Publish(message, token);
        await _emailTokenService.CommitChangesAsync(token);
    }
}

public class EmailTokenService : BaseService<EmailToken>
{
    public EmailTokenService(IUnitOfWorkService uow) : base(uow)
    {
    }
    public async Task<CreateEmailToken> CreateModel(
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

    public async Task<CreateEmailToken> CreateModel(DateTime expiry, string email)
    {
        return new CreateEmailToken
        {
            Expiry = expiry,
            TokenType = "",
            TokenValue = TokenGenerator.GenerateRandomToken(),
            Email = email,
        };
    }

    public string GetRandomToken => TokenGenerator.GenerateRandomToken();
    public async Task StoreToken(CreateEmailToken payload, CancellationToken token)
    {
        var model = new EmailToken
        {
            Email = payload.Email,
            TokenValue = payload.TokenValue,
            TokenType = payload.TokenType,
            Expiry = payload.Expiry,
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