namespace DTR.Core;

public class RegularHolidayStrategy : IHolidayFilterStrategy
{
    public bool IsApplicable(HolidayInfo holiday, Employee employee)
        => holiday.HolType == HolidayType.LEGAL;
     
}