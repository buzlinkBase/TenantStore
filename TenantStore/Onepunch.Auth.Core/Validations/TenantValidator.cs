using FluentValidation;
using OnePunch.Auth.Domain.Entities;

namespace OnePunch.Auth.Core.Validations
{
    public class UserValidator : AbstractValidator<User>
    {
        public UserValidator(IUnitOfWorkService service)
        {
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
                    var result = service.Context.Users.FirstOrDefault(x => x.Email == payload.Email && x.Id != payload.Id);
                    return result == null;
                }).WithMessage("Email must be unique.");
        }
    }
}
