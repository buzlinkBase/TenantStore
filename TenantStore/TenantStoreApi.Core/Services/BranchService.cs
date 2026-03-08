using AutoMapper;
using BuzlinkRepository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Exceptions;
using Polly;
using TenantStoreApi.Core.Validations;

namespace TenantStoreApi.Core.Services;

public class BranchService : BaseService<Branch>
{
    private readonly IMapper _mapper;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPublishEndpoint _publisher;

    public BranchService(IUnitOfWorkService service, IMapper mapper,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher) : base(service)
    {
        _mapper = mapper;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
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

    private void GenerateCode(Branch model)
    {
        var codeCount = GetQueryable().Count();
        if (model != null && string.IsNullOrEmpty(model.Code))
        {
            model.Code = codeCount.FormatCode();
        }
    }

    public async Task AddAsync(CreateBranch model, CancellationToken token)
    {
        var branch = _mapper.Map<Branch>(model);
        GenerateCode(branch);
        await CreateAsync(branch, token);
        await UoW.SaveChangesAsync(token);
        await PublishAsync(branch, token);
    }
    public async Task UpdateAsync(Guid Id, UpdateBranch model, CancellationToken token)
    {
        var branch = _mapper.Map<Branch>(model);
        GenerateCode(branch);
        branch.Id = Id;
        await ModifyAsync(branch, token);
        await PublishAsync(branch, token);
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

    public async Task<bool> Delete(Guid Id, CancellationToken token)
    {
        var branch = await GetOneAsync(Id, token);
        if (branch == null) return true;
        branch.Status = "Deleted";
        await RemoveAsync(branch);
        await PublishAsync(branch, token);
        return true;
    }

    private async Task PublishAsync(Branch branch, CancellationToken token)
    {
        _tenantProvider.SetTenantId(branch.TenantId);
        await _publisher.Publish(new BranchModel
        {
            Id = branch.Id,
            TenantId = branch.TenantId,
            Code = branch.Code,
            Name = branch.Name,
            Address = branch.Address,
            Contact = branch.Contact,
            ManagerName = branch.ManagerName,
            Status = branch.Status,
            DeletedAt = branch.DeletedAt
        }, token);
    }
}

