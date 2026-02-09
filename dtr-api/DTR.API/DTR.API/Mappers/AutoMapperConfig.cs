using AutoMapper;

namespace DTR.Api ;
public static class MapperConfig
{
    public static IMapper RegisterMappings()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.SourceMemberNamingConvention = new LowerUnderscoreNamingConvention();
            cfg.DestinationMemberNamingConvention = new PascalCaseNamingConvention();
            cfg.AddProfile<AutoMapperProfile>();
        });
        return config.CreateMapper();
    }
}
public static class MapperProvider
{
    public static IMapper Instance { get; } = MapperConfig.RegisterMappings();
}