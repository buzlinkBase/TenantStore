namespace DTR.Core;

public class NightDiffTimeSplitter
{
    public static List<CalculatedDateRange> Split(DateTime shiftStart, DateTime shiftEnd)
    {
        var results = new List<CalculatedDateRange>();

        if (shiftEnd <= shiftStart)
            return results; // No time range to evaluate

        var current = shiftStart.Date.AddDays(-1);
        while (current < shiftEnd)
        {
            var nightStart = current.AddHours(22);         // 10 PM
            var nightEnd = current.AddDays(1).AddHours(6); // 6 AM next day

            if (shiftEnd > nightStart && shiftStart < nightEnd)
            {
                var overlapStart = (shiftStart > nightStart) ? shiftStart : nightStart;
                var overlapEnd = (shiftEnd < nightEnd) ? shiftEnd : nightEnd;

                if (overlapStart < overlapEnd)
                    results.Add(new CalculatedDateRange(overlapStart, overlapEnd));
            }

            current = current.AddDays(1);
        }
        return results;
    }
}
public class NightDiffChecker
{
    public static bool IsDutyNightDiff(DateTime start, DateTime end)
    {
        var result = NightDiffTimeSplitter.Split(start, end);
        return result.Count > 0;
    }
    public static double GetTotalMinutes(DateTime start, DateTime end)
    {
        var nightDiffMinutes = NightDiffTimeSplitter.Split(start, end);
        var total = nightDiffMinutes.Sum(x => TimeRangeCalculator.GetTotalMinutes(x.Start, x.End));
        return total;
    }
}

public class NightDiffCalculator
{
    public static TimeRange Calculate(TimeRangeCollection timeRecords)
    { 
        if (timeRecords == null || timeRecords.Count == 0)
            return new TimeRange();

        double totalTime = 0;
        var trCollection = new TimeRangeCollection();
        for (int i = 0; i < timeRecords.Count; i++)
        {
            var timeRange = timeRecords[i];
            if (timeRange?.StartTime == null || timeRange.EndTime == null)
                continue;

            var splitRanges = NightDiffTimeSplitter.Split(timeRange.StartTime, timeRange.EndTime);
            if (splitRanges == null || splitRanges.Count == 0)
                continue;

            for (int j = 0; j < splitRanges.Count; j++)
            {
                var segment = splitRanges[j];
                totalTime += TimeRangeCalculator.GetTotalMinutes(segment.Start, segment.End);
                trCollection.Add(new TimeRecord(segment.Start, segment.End));
            }
        }
        return new TimeRange(totalTime, trCollection);
    }
    public static TimeRange Calculate(TimeRange range)
    {
        if (range.IsEmpty()) return TimeRange.Empty;
        var finalRange= Calculate(range.TimeRecords);
        return finalRange;
    }
    public static TimeRange Calculate(DateTime startTime, DateTime endTime)
    {
        double totalTime = NightDiffTimeSplitter.Split(startTime, endTime)
                     .Sum(x => TimeRangeCalculator.GetTotalMinutes(x.Start, x.End));

        return new TimeRange(totalTime, TimeRangeSetter.SetTimeRangeCollection(startTime, endTime));

    }
}