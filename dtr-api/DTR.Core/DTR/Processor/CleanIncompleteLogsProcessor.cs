namespace DTR.Core;

public class CleanIncompleteLogsProcessor : IDTRProcessor<ColumnarLogModel>
{
    public ColumnarLogModel? Process(DTRProcessorPayload payload)
    {
        var logs = new LogsWithNoPairIdentifier(payload).GetIncompleteAtt();
        if (!logs.Any()) return null;
        var mapper = IncompleteLogMapperFactory.Create(payload);
        return mapper.MapData(payload, logs, payload.Data.CurrentDate);
    }
}

internal class LogsWithNoPairIdentifier
{
    private readonly DTRProcessorPayload _payload;
    public LogsWithNoPairIdentifier(DTRProcessorPayload payload)
    {
        _payload = payload;
    }
    public List<Attendance> GetIncompleteAtt()
    {
        if (_payload.Data.CurrentAttendance.Count % 2 == 0)
        {
            return [];
        }
        return _payload.Data.CurrentAttendance;
    }
}

internal class FixMapper : IIncompleteLogShiftMapper
{
    private readonly DTRProcessorPayload _payload;

    public FixMapper(DTRProcessorPayload payload)
    {
        _payload = payload;
    }

    public ColumnarLogModel MapData(DTRProcessorPayload payload, List<Attendance> attendances, DateOnly payrollDate)
    {
        var shift = _payload.Data.CurrentShift;
        var am = AttendanceHelper.GetPunchNear(shift.StartTime, attendances, 150);
        var pm = AttendanceHelper.GetPunchNear(shift.EndTime, attendances, 150);

        Attendance? lunchOut = null;
        Attendance? lunchIn = null;

        if (shift.LunchBreakOption ==  BreakMode.UNPAID_BREAK)
        {
            lunchOut = AttendanceHelper.GetPunchNear(shift.LunchStartTime!.Value, attendances, 30);
            lunchIn = AttendanceHelper.GetPunchNear(shift.LunchEndTime!.Value, attendances, 30);
        }

        // Placeholder for OT logic
        Attendance? otIn = null;
        Attendance? otOut = null;
        var dtr = new ColumnarLogModel
        {
            EmpNo = payload.Data.Employee.BioId.ToString(),
            FullName = payload.Data.Employee.FullName,
            EmployeeId = payload.Data.Employee.Id,
            WorkDate = payrollDate,
            ShiftName = shift.ShiftName,
            Log1 = am != null ? AttInfo.Set(am.Id, am.WorkDateTime) : null,
            Log2 = lunchOut != null ? AttInfo.Set(lunchOut.Id, lunchOut.WorkDateTime) : null,
            Log3 = lunchIn != null ? AttInfo.Set(lunchIn.Id, lunchIn.WorkDateTime) : null,
            Log4 = pm != null ? AttInfo.Set(pm.Id, pm.WorkDateTime) : null,
            Log5 = otIn != null ? AttInfo.Set(otIn.Id, otIn.WorkDateTime) : null,
            Log6 = otOut != null ? AttInfo.Set(otOut.Id, otOut.WorkDateTime) : null
        };
        return dtr;
    }
}
internal class FlexiMapper : IIncompleteLogShiftMapper
{
    private readonly DTRProcessorPayload _payload;

    public FlexiMapper(DTRProcessorPayload payload)
    {
        _payload = payload;
    }

    public ColumnarLogModel MapData(DTRProcessorPayload payload, List<Attendance> attendances, DateOnly payrollDate)
    { 
        var dtr = new ColumnarLogModel
        {
            EmpNo = payload.Data.Employee.BioId.ToString(),
            FullName = payload.Data.Employee.FullName,
            EmployeeId = payload.Data.Employee.Id,
            ClientId = payload.Data.Employee.ClientId,
            PayrollGroupId = payload.Data.Employee.PayrollGroupId,
            DepartmentId = payload.Data.Employee.DepartmentId,
            WorkDate = payrollDate,
            ShiftName = _payload.Data.CurrentShift.ShiftName,
            Department = _payload.Data.Employee?.DepartmentName   ?? "",
            ShiftStart = _payload.Data.CurrentShift.StartTime,
            ShiftEnd = _payload.Data.CurrentShift.EndTime,
            BreakOut = _payload.Data.CurrentShift.LunchStartTime,
            BreakIn = _payload.Data.CurrentShift.LunchEndTime,
            Log1 = attendances.ElementAtOrDefault(0)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log2 = attendances.ElementAtOrDefault(1)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log3 = attendances.ElementAtOrDefault(2)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log4 = attendances.ElementAtOrDefault(3)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log5 = attendances.ElementAtOrDefault(4)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log6 = attendances.ElementAtOrDefault(5)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log7 = attendances.ElementAtOrDefault(6)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log8 = attendances.ElementAtOrDefault(7)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log9 = attendances.ElementAtOrDefault(8)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log10 = attendances.ElementAtOrDefault(9)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log11 = attendances.ElementAtOrDefault(10)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log12 = attendances.ElementAtOrDefault(11)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log13 = attendances.ElementAtOrDefault(12)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log14 = attendances.ElementAtOrDefault(13)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log15 = attendances.ElementAtOrDefault(14)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log16 = attendances.ElementAtOrDefault(15)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log17 = attendances.ElementAtOrDefault(16)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log18 = attendances.ElementAtOrDefault(17)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log19 = attendances.ElementAtOrDefault(18)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime)),
            Log20 = attendances.ElementAtOrDefault(19)?.Let(a => AttInfo.Set(a.Id, a.WorkDateTime))
        };
        return dtr;
    }
} 