namespace DTR.Core;


public record TimeRange(double TotalMinutes, TimeRangeCollection TimeRecords)
{
    private Dictionary<string, object> Metadata { get; set; } = new();
    public TimeRange(double totalMinutes) : this(totalMinutes, new TimeRangeCollection()) { }
    public TimeRange() : this(0, new TimeRangeCollection()) { }
    public static TimeRange Empty => new TimeRange();
    public static TimeRange Set(double minutes, DateTime startTime, DateTime endTime) => new TimeRange(minutes, TimeRangeSetter.SetTimeRangeCollection(startTime, endTime));
    public static TimeRange Set(double minutes, TimeRangeCollection timeRecords) => new TimeRange(minutes, timeRecords);

    public static TimeRange Set(TimeRangeCollection timeRecords)
    {
        var totalMinutes = timeRecords.TotalMinutes();
        return new TimeRange(totalMinutes, timeRecords);
    }

    // Optional metadata container
    public void SetMetaData<T>(string key, T value) => Metadata[key] = value;
    public T? GetMetaData<T>(string key) => Metadata.TryGetValue(key, out var val) ? (T)val : default;
    public static TimeRange operator +(TimeRange left, TimeRange right)
    {
        if (left.IsEmpty() && right.IsEmpty())
            return TimeRange.Empty;

        if (left.IsEmpty()) return right;
        if (right.IsEmpty()) return left;

        var combinedRecords = new TimeRangeCollection();
        combinedRecords.AddRange(left.TimeRecords);
        combinedRecords.AddRange(right.TimeRecords);

        // Filter out zero-length records 
        var cleanedCollection = combinedRecords
            .MergeOverlapping()
            .ToTimeRangeCollection();
        //generate new timeRange from Collection
        return cleanedCollection.ToTimeRange();
    }
}

public class TimeRangeCollection : List<TimeRecord>
{
    public TimeRangeCollection() { }
    public static TimeRangeCollection Empty => new TimeRangeCollection();
    public TimeRangeCollection(IEnumerable<TimeRecord> records)
        : base(records ?? Enumerable.Empty<TimeRecord>())
    {
    }
}

public class TimeRecord
{
    public TimeRecord()
    {
        StartTime = DateTime.MinValue;
        EndTime = DateTime.MinValue;
    }

    public TimeRecord(DateTime startTime, DateTime endTime, string? tag = "")
    {
        StartTime = startTime;
        EndTime = endTime;
        Tag = tag ?? string.Empty;
    }

    public static TimeRecord Set(DateTime startTime, DateTime endTime, string? tag) => new TimeRecord(startTime, endTime, tag);
    public static TimeRecord? Null()
    {
        return null;
    }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double TotalMinutes
    {
        get
        {
            return TimeRangeCalculator.GetTotalMinutes(StartTime, EndTime);
        }
    }
    public string Tag { get; set; }

}