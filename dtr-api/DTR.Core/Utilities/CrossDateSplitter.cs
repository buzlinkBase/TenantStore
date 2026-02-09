namespace DTR.Core;

public static class CrossDateSplitter
{
    public static List<CalculatedDateRange> Split(DateTime startDate, DateTime endDate)
    {
        if (!CrossDateChecker.IsCrossDate(startDate, endDate))
            return [];

        var midnight = endDate.Date;
        var slices = new List<CalculatedDateRange>(2);

        if (startDate < midnight)
            slices.Add(new CalculatedDateRange(startDate, midnight));

        if (endDate > midnight)
            slices.Add(new CalculatedDateRange(midnight, endDate));

        return slices;
    }
} 
public class CalculatedDateRange
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public CalculatedDateRange(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }
}
public class CrossDateChecker
{
    public static bool IsCrossDate(DateTime start, DateTime endDate) => endDate.Date > start.Date || endDate.Date < start.Date;
}