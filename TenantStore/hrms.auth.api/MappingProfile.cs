using AutoMapper;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.DTO;

namespace OnePunch.Auth.Api
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {

            CreateMap<TenantCreatedPayload, User>();
            CreateMap<UpdateUser, User>();
            CreateMap<User, UserModel>();
        }
    }
}
