using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.DTOs;
using TenantStoreApi.Domain.Entities;

namespace TenantStoreApi.Core.Validations
{
    public class TenantValidator : AbstractValidator<Tenant>
    {
        public TenantValidator(IUnitOfWorkService service)
        {
            RuleFor(x => x.CompanyName)
                .NotNull().WithMessage("Company Name is required")
                .NotEmpty().WithMessage("Company Name is required")
                ;

            RuleFor(x => x.Email)
           .NotNull().WithMessage("Email is required")
           .NotEmpty().WithMessage("Email is required")
           ;

            RuleFor(x => x)
               .Must(payload => payload != null)
               .WithMessage("Invalid account information");

            RuleFor(x => x)
                .Must((payload) =>
                {
                    var result = service.Repository.FindOne<Tenant>(x => x.Email == payload.Email && x.Id != payload.Id);
                    return result == null;
                }).WithMessage("Email must be unique.");
        }
    }
}
