namespace DTR.Core;

public class RestDayChecker
{
    public static bool IsRestDay(DTRProcessorPayload _payload, DateOnly payrollDate)
    {
        var restDays  = _payload.Data.CurrentDayoffs;
        if (_payload == null || restDays == null || restDays.Count() == 0) return false;
        var key = new ResDaykey(_payload.Data.Employee.Id, payrollDate);
        return restDays.TryGetValue(key, out var currentDayoff) && currentDayoff != null;
    }
    public static bool IsRestDay(DTRProcessorPayload _payload)
    {
        return IsRestDay(_payload, _payload.Data.CurrentDate);
    }
}