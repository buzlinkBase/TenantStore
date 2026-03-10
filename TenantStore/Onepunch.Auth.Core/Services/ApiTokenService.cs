using AutoMapper;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using Serilog; 

namespace Onepunch.Auth.Core.Services;

public class ApiTokenService : BaseService<ApiToken>
{
    private readonly IUnitOfWorkService _service;
    private readonly IMapper _mapper;
    public ApiTokenService(IUnitOfWorkService service,
        IMapper mapper) : base(service)
    {
        _service = service;
        _mapper = mapper;
    }
    public async Task<ApiTokenModel?> AddTokenAsync(CreateToken model, CancellationToken ct)
    {
        try
        {
            if (model == null) return default;
            var token = TokenGenerator.Generate(model.Email, model.UserId);
            var newToken = new ApiToken
            {
                Description = model.Description,
                TokenType = model.TokenType,
                ExpirationType = model.ExpirationType,
                ExpiredAt = model.ExpireAt,
                Status = TokenStatus.Active,
                Token = token,
                TenantId = model.TenantId,
                UserId = model.UserId
            };
            await Repository.AddAsync(newToken, ct);
            return new ApiTokenModel
            {
                ExpireAt = model.ExpireAt,
                Token = token,
            };
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex.Message);
            throw new Exception("Unable to create token");
        }
    }

    public async Task<ApiTokenModel?> GetApiTokens(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return await GetQueryable(x =>
                x.TenantId == tenantId
                && x.Status == TokenStatus.Active
                && (x.ExpirationType == TokenExpirationType.None || x.ExpiredAt > now))
            .Select(x => new ApiTokenModel
            {
                Token = x.Token,
                ExpireAt = x.ExpiredAt
            })
            .FirstOrDefaultAsync();
    }
    public async Task<ApiTokenModel?> GetApiToken(Guid tenantId, TokenType tokenType)
    {
        var now = DateTime.UtcNow;
        return await GetQueryable(x =>
                x.TenantId == tenantId
                && x.TokenType == tokenType
                && x.Status == TokenStatus.Active
                && (x.ExpirationType == TokenExpirationType.None || x.ExpiredAt > now))
            .Select(x => new ApiTokenModel
            {
                Token = x.Token,
                ExpireAt = x.ExpiredAt
            })
            .FirstOrDefaultAsync();
    }
}