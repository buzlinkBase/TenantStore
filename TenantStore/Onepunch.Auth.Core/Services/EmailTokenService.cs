using AutoMapper;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;

namespace Onepunch.Auth.Core.Services;

public class EmailTokenService:BaseService<EmailToken>
{
    private readonly TenantService _tenantService;
    public EmailTokenService(
        IUnitOfWorkService uow,
        TenantService tenantService):base(uow)
    {
        _tenantService = tenantService;
    }
    public async Task<CreateEmailToken> CreateModelAsync(string TokenType,  DateTime expiry, string email)
    {
        var tenant = await _tenantService.GetInfoAsync();
        if (tenant == null) throw new Exception("Tenant not found");
        var tenantId = Guid.Parse(tenant.TenantId);
        var token = TokenGenerator.Generate(tenantId,  email);
        return new CreateEmailToken
        {
            Expiry = expiry,
            TokenType = TokenType,
            TokenValue = token,
            TenantId = tenantId,
            Email = email,
            TenantName=tenant.Name  
        };
    }

    public async Task StoreToken(CreateEmailToken payload)
    {
        var model = new EmailToken
        {
            Email = payload.Email,
            TokenValue = payload.TokenValue,
            TokenType = payload.TokenType,
            Expiry = payload.Expiry,
            Status = "Active",
            TenantId = payload.TenantId,
            UserId = payload.UserId,
        };
    }
}

public class CreateEmailToken
{
    public DateTime Expiry { get; set; }
    public string TokenType { get; set; }
    public string TokenValue { get; set; }
    public string Email { get; set; }
    public Guid TenantId { get; set; }
    public string? TenantName  { get; set; }
    public Guid? UserId { get; set; }
}