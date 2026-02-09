namespace DTR.Core;
public class TimeRangeSetter
{
    public static TimeRangeCollection SetTimeRangeCollection(TimeRecord range)
    {
        return new TimeRangeCollection { range };
    }
    public static TimeRangeCollection SetTimeRangeCollection(DateTime startDateTime, DateTime endDateTime)
    {
        return SetTimeRangeCollection(new TimeRecord { StartTime = startDateTime, EndTime = endDateTime });
    }
    public static TimeRangeCollection SetTimeRangeCollection(List<Attendance> paired)
    {
        TimeRangeCollection timeRecords = new TimeRangeCollection();
        for (int i = 0; i < paired.Count - 1; i += 2)
        {
            if (i + 1 < paired.Count && paired[i + 1] == null) continue;
            timeRecords.Add(new TimeRecord
            {
                StartTime = paired[i].WorkDateTime,
                EndTime = paired[i + 1].WorkDateTime
            });
        }
        return timeRecords;
    }
}
