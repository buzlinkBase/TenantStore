using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Exceptions;
using TenantStoreApi.Core.Validations;

namespace TenantStoreApi.Core.Services;

public class BranchService : BaseService<Branch>
{
    private readonly IMapper _mapper;
    public BranchService(IUnitOfWorkService service, IMapper mapper) : base(service)
    {
        _mapper = mapper;
    }

    protected override async Task<EvaluationResult> CreateValidatorAsync(Branch model, CancellationToken token)
    {
        await base.CreateValidatorAsync(model, token);
        Guard.ThrowIfNull(model, nameof(model));
        Guard.ThrowIfEmpty(model.Name, nameof(model.Name));
        var fluentValResult = await new BranchValidator(UoW).ValidateAsync(model);
        var result = EvaluationResult.Check(fluentValResult);
        Guard.ThrowIfError(result);
        return result;
    }
    public async Task AddAsync(CreateBranch model, CancellationToken token)
    {
        var branch = _mapper.Map<Branch>(model);
        await CreateAsync(branch, token);
        await UoW.SaveChangesAsync(token);
    }
    public async Task UpdateAsync(Guid Id, UpdateBranch model, CancellationToken token)
    {
        var branch = _mapper.Map<Branch>(model);
        branch.Id = Id;
        await ModifyAsync(branch, token);
    }
    public async Task<List<BranchModel>> FindAllAsync(Guid tenantId)
    {
        var result = await GetQueryable()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        return _mapper.Map<List<BranchModel>>(result);
    }
    public async Task<BranchModel?> FineOneAsync(Guid Id, CancellationToken token)
    {
        var result = await GetOneAsync(Id, token);
        return _mapper.Map<BranchModel?>(result);
    }
    public async Task<bool> Delete(Guid Id)
    {
        await RemoveAsync(Id);
        return true;
    }
}

