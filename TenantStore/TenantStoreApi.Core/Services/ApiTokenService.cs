using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace TenantStoreApi.Core.Services;

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
    public async Task<ApiTokenModel?> GenerateToken(CreateToken model)
    {
        try
        {
            if (model == null) return default;
            var token = TokenGenerator.Generate(model.TenantId, model?.Email ?? "");
            var newToken = new ApiToken
            {
                Description=model.Description,
                TokenType = model.TokenType,
                ExpirationType = model.ExpirationType,
                ExpiredAt = model.ExpireAt,
                Status = TokenStatus.Active,
                Token = token,
                TenantId = model.TenantId,
                UserId = model.UserId
            };
            await Repository.AddAsync(newToken);
            CommitChanges();
            return new ApiTokenModel
            {
                ExpireAt = model.ExpireAt,
                Token = token,
            };
        }
        catch (Exception ex)
        {
            throw new Exception("Unable to create token");
            Log.Logger.Error(ex.Message);
        }
    }

    public async Task<ApiTokenModel?> GetApiToken(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return await GetQueryable(x =>
                x.TenantId == tenantId
                && x.TokenType == TokenType.API
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


