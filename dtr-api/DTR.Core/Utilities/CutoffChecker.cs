namespace DTR.Core;


public class CutoffChecker
{
    public static bool IsPayrollCuttoffStart(CurrentShift lastShift,DateOnly payrollStart)
    {
        return lastShift.IsCrossDate && payrollStart == DateOnly.FromDateTime(lastShift.EndTime);
    }
    public static bool IsPayrollCuttoffEnd(CurrentShift CurrentShift,DateOnly payrollEnd)
    {
        return CurrentShift.IsCrossDate && payrollEnd < DateOnly.FromDateTime(CurrentShift.EndTime);
    }
}
