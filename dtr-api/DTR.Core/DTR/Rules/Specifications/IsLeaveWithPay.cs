namespace DTR.Core;

public class IsLeaveWithPay : IRuleSpecification
{
    private readonly DateOnly _curDate;
    public IsLeaveWithPay(DateOnly curDate)
    {
        _curDate = curDate;
    }
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        //check if employee is on leave with pay
        var currentLeave = context.Payload.Provider.LeaveProvider.GetLeave(_curDate);
        return currentLeave != null && currentLeave.PayType==PayType.WithPay;
    }
}
