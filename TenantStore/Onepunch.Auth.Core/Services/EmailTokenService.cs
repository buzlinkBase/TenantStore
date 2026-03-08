using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
namespace Onepunch.Auth.Core.Services;

public class EmailTokenService : BaseService<EmailToken>
{
    private readonly TenantService _tenantService;
    public EmailTokenService(IUnitOfWorkService uow,
        TenantService tenantService) : base(uow)
    {
        _tenantService = tenantService;
    }
    public async Task<CreateEmailToken> CreateModelAsync(string TokenType, DateTime expiry, string email, string? emailToken =  null)
    {
        var tenant = await _tenantService.GetGrpcBgInfoAsync();
        var tenantId = Guid.Parse(tenant.TenantId);
        if (tenant == null) throw new Exception("Organization not found");
        var token = emailToken ?? TokenGenerator.Generate(tenantId, email);
        return new CreateEmailToken
        {
            Expiry = expiry,
            TokenType = TokenType,
            TokenValue = token,
            TenantId = tenantId,
            Email = email,
            TenantName = tenant.Name
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
            TenantId = payload.TenantId,
            UserId = payload.UserId,
        };
        await Repository.AddAsync(model, token);
    }
}

public class CreateEmailToken
{
    public DateTime Expiry { get; set; }
    public string TokenType { get; set; }
    public string TokenValue { get; set; }
    public string Email { get; set; }
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public Guid? UserId { get; set; }
}