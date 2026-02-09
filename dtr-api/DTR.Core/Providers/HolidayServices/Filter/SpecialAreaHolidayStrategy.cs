namespace DTR.Core;

public class SpecialAreaHolidayStrategy : IHolidayFilterStrategy
{
    public bool IsApplicable(HolidayInfo holiday, Employee employee)
        => holiday.HolType == HolidayType.SPECIAL
           && holiday.AreaId != Guid.Empty
           && employee.AreaId.HasValue
           && holiday.AreaId == employee.AreaId.Value;
}