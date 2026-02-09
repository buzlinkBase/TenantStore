
using Adms.api.Controllers;
using Adms.api.Domain.Entties;
using Adms.api.Domain.ValueObjects;
using Adms.api.Exceptions;
using AutoMapper;
using AutoMapper.QueryableExtensions;

namespace Adms.api.Services;
public class AttendanceService : BaseService<Attendance>
{
    private readonly IMapper _mapper;

    public AttendanceService(IUnitOfWorkService service,
        IMapper mapper) : base(service)
    {
        _mapper = mapper;
    }
    protected override Task<ValidationResult> CreateValidator(Attendance model)
    {
        return base.CreateValidator(model);
    }
    public async Task Create(List<Attendance>  attendances)
    {
        foreach (var attendance in attendances)
        {
            await CreateAsync(attendance);
        }
        await CommitChangesAsync();
    }
    public async Task Create(Attendance attendance) 
    {
        await CreateAsync(attendance);
        await CommitChangesAsync();
    }


    public async Task<List<AttendanceModel>> Download(AttendanceRequest request) 
    {
        var atts=_uow.Context.Attendances
            .Where(x=>x.SN==request.SN)
            .ProjectTo<AttendanceModel>(_mapper.ConfigurationProvider)
            .ToList();

        return atts;

    }
}

