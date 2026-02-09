using Adms.api.Domain.Entties;
using Adms.api.Domain.ValueObjects;
using AutoMapper;

namespace Adms.api;
public class AspAutoMapperProfile : Profile
{
    public AspAutoMapperProfile()
    {
        CreateMap<Attendance, AttendanceModel>().ReverseMap();
    }
} 
 