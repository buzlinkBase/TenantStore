namespace DTR.Core;

public interface IHolidayFilterStrategy
{
    bool IsApplicable(HolidayInfo holiday, Employee employee);
}
