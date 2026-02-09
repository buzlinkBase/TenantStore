using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenantStoreApi.Core.Validations
{
    public class BranchValidator : AbstractValidator<Branch>
    {
        public BranchValidator(IUnitOfWorkService service)
        {
            RuleFor(x => x.Name)
             .NotEmpty().WithMessage("Branch Name is required")
             .NotNull().WithMessage("Branch Name is required");

            RuleFor(x => x.TenantId)
            .Must(x => x != Guid.Empty).WithMessage("Invalid Organization");

            RuleFor(x => x)
            .Must(xx =>
            {
                var exist = service.Context.Branches.FirstOrDefault(b => b.TenantId == xx.TenantId
                    && b.Name == xx.Name
                    && b.Id != xx.Id);
                return exist == null;
            })
            .WithMessage("Branch name already exists for this tenant");
        }
    }
}
