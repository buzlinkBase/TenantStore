 
namespace DTR.Core;

internal class IsShowWorkOnHolidayInRegColumn : IColumnDisplaySpec
{
    public bool IsSatisfiedBy(DisplayContext context) => context.TimeContext.Payload.Data.CompanyPolicy.IsHolPlusReg;
}