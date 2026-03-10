using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;

namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedWorker : IConsumer<TenantCreatedPayload>
{
    private readonly IConfiguration _configuration;
    private readonly ITenantProvider _tenantProvider;
    private readonly UserService _userService;
    private readonly PasswordCrypto _passwordCrypto;
    private readonly EmailTokenService _emailTokenService;
    private readonly IPublishEndpoint _publisher;
    private readonly Domains _domainOptions;

    public TenantCreatedWorker(
        ITenantProvider tenantProvider,
        UserService userService,
        PasswordCrypto passwordCrypto,
        EmailTokenService emailTokenService,
        IOptions<Domains> domainOptions,
        IPublishEndpoint publisher,
        IConfiguration configuration)
    {
        _configuration = configuration;
        _tenantProvider = tenantProvider;
        _userService = userService;
        _passwordCrypto = passwordCrypto;
        _emailTokenService = emailTokenService;
        _publisher = publisher;
        _domainOptions = domainOptions.Value;
    }

    public async Task Consume(ConsumeContext<TenantCreatedPayload> context)
    {
        var model = context.Message;
        string plainPassword = _passwordCrypto.Decrypt(model.Password);
        model.Password = plainPassword;
        _tenantProvider.SetTenantId(model.TenantId);    
        var response = await _userService.RegisterTenantAdmin(model);
        if (!response.result.Succeeded)
        {
            var errors = string.Join(", ", response.result.Errors.Select(e => e.Description));
            throw new Exception($"DB Registration failed: {errors}");
        }
        //notify user
        var exp = DateTime.UtcNow.AddDays(1);
        var tokenModel = await _emailTokenService.CreateModelAsync("user.created", exp, model.Email);
        tokenModel.UserId = response.user.Id;
        await _emailTokenService.StoreToken(tokenModel, context.CancellationToken);
        var emailDomain = ComposePayload(response.user, tokenModel);
        await _publisher.Publish(emailDomain, context.CancellationToken);
        await _userService.CommitChangesAsync(context.CancellationToken);
    }

    private UserEmailPayload ComposePayload(User user, CreateEmailToken tokenInfo)
    {
        var token = tokenInfo.TokenValue;
        return new UserEmailPayload
        {
            Token = token,
            Email = user.Email!,
            TenantId = user.TenantId,
            UserId = user.Id,
            TenantName = tokenInfo?.TenantName ?? "",
            AppName = _configuration["AppName"] ?? "OnePunch",
            FullName = user.Name ?? "User",
            Expiry = tokenInfo?.Expiry ?? DateTime.UtcNow.AddDays(2),
            IssuedAt = DateTime.UtcNow,
            Purpose = "Tenant account confirmation",
            ConfirmationRoute = $"{_domainOptions.AuthDomain}/api/v1/users/confirm-email?token={token}"
        };
    }
}
