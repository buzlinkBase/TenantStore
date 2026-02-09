using AutoMapper;
namespace DTR.Api;

public class BaseMapperProfile : Profile
{
    public BaseMapperProfile()
    {
        SourceMemberNamingConvention = new LowerUnderscoreNamingConvention();
        DestinationMemberNamingConvention = new PascalCaseNamingConvention();
    }
}

public class AutoMapperProfile : BaseMapperProfile
{
    public AutoMapperProfile()
    {
    }
}
