namespace DTR.Core;

public class HolidayFilterChain
{
    private readonly List<IHolidayFilterStrategy> _strategies;

    public HolidayFilterChain()
    {
        _strategies = new List<IHolidayFilterStrategy>
        {
            new RegularHolidayStrategy(),
            new SpecialAreaHolidayStrategy(),
            new SpecialNationalHolidayStrategy(),
        };
    }

    public bool IsApplicable(HolidayInfo holiday, Employee employee)
        => _strategies.Any(strategy => strategy.IsApplicable(holiday, employee));
}