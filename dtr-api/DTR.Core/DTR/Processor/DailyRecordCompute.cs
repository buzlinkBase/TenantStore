namespace DTR.Core;

public class DailyRecordCompute
{
    private readonly DTRContextModel _dtrContext;
    public DailyRecordCompute(DTRContextModel dtrContext)
    {
        _dtrContext = dtrContext;
    }

    public ObjectCollection<T> ProcessDailyRecords<T>(IDTRProcessor<T> processor,
        DateOnly fromDate, DateOnly ToDate,  CancellationToken token, 
        IncludeNullResponse ignoreNull = IncludeNullResponse.Include,
        bool processOnlyPairedAtt = true) where T : class, new()
    {
        var dtrCollection = new ObjectCollection<T>();
        int totalDays = (ToDate.ToDateTime(TimeOnly.MinValue) - fromDate.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var totalCount = (_dtrContext.Employees.Count * totalDays) / 100;
        totalCount = (totalCount == 0 ? 1 : totalCount);
        var counter = 0;
        foreach (var employee in _dtrContext.Employees)
        {
            counter += 1;
            for (var curDate = fromDate; curDate <= ToDate; curDate = curDate.AddDays(1))
            {
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }
                counter += 1;
                var TotalCount = (counter / totalCount);
                var dtrPayload = CurrentDayDTRPayload.SetPayload(_dtrContext, employee, curDate, processOnlyPairedAtt);
                dtrPayload.Data.PayrollStartDate = fromDate;
                var dtr = processor.Process(dtrPayload);
                if (ignoreNull == IncludeNullResponse.Ignore && dtr == null) continue;
                var data = dtr ?? new T();
                dtrCollection.Add(data);
            }
        }
        return dtrCollection;
    }
}
