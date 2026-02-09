namespace DTR.Core;

public class SpecialNationalHolidayStrategy : IHolidayFilterStrategy
{
    public bool IsApplicable(HolidayInfo holiday, Employee employee)
        => holiday.HolType == HolidayType.SPECIAL && holiday.AreaId == Guid.Empty;
}