using Mapster;
namespace OnePunch.Auth.Api;

//public class MappingProfile : Profile
//{
//    public MappingProfile()
//    {

//        CreateMap<TenantCreatedPayload, User>();
//        CreateMap<UpdateUser, User>();
//        CreateMap<User, UserModel>();
//    }
//}

public class MappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        //config.NewConfig<TenantCreatedPayload, User>();
        //config.NewConfig<UpdateUser, User>();
        //config.NewConfig<User, UserModel>();
        //    .Map(dest => dest.CustomerFullName, src => $"{src.User.FirstName} {src.User.LastName}")
        //    .Map(dest => dest.TotalAmount, src => src.Price * src.Quantity);
        config.Default.NameMatchingStrategy(NameMatchingStrategy.Flexible);
    }
}
