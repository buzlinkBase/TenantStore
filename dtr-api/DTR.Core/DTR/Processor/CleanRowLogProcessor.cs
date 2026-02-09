namespace DTR.Core;

public class CleanRowLogProcessor : IDTRProcessor<List<RowLogModel>>
{
    public List<RowLogModel> Process(DTRProcessorPayload payload)
    {
        var logs = payload.Data.CurrentAttendance;
        if (!logs.Any()) return null;
        return new ExtracLogsToRows(payload).MapData(payload, logs, payload.Data.CurrentDate);
    }
}

internal class ExtracLogsToRows
{
    private readonly DTRProcessorPayload _payload;

    public ExtracLogsToRows(DTRProcessorPayload payload)
    {
        _payload = payload;
    }

    public List<RowLogModel> MapData(DTRProcessorPayload payload,
        List<Attendance> attendances,
        DateOnly payrollDate)
    {
        var dateLogs = new List<RowLogModel>();
        foreach (var item in attendances)
        {
            var dtr = new RowLogModel
            {
                EmpNo = payload.Data.Employee.BioId.ToString(),
                FullName = payload.Data.Employee.FullName,
                EmployeeId = payload.Data.Employee.Id,
                WorkDate = payrollDate,
                ShiftName = _payload.Data.CurrentShift.ShiftName,
                Department = _payload.Data.Employee?. DepartmentName  ?? "",
                ShiftStart = _payload.Data.CurrentShift.StartTime,
                ShiftEnd = _payload.Data.CurrentShift.EndTime,
                BreakOut = _payload.Data.CurrentShift.LunchStartTime,
                BreakIn = _payload.Data.CurrentShift.LunchEndTime,
                Log1 = item?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            };
            dateLogs.Add(dtr);
        }
        return dateLogs;
    }
}