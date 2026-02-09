namespace DTR.Core;

public class CleanForm48Processor : IDTRProcessor<Form48ResultModel>
{
    private static Dictionary<CalculatorsType, BaseTimeCalculator> CreateCalculators(DTRProcessorPayload payload)
    {
        return new Dictionary<CalculatorsType, BaseTimeCalculator>
        {
            { CalculatorsType.RegularTime, RegularTimeCalculatorFactory.Create(payload) },
        };
    }

    public Form48ResultModel? Process(DTRProcessorPayload payload)
    {
        var calculators = CreateCalculators(payload);
        var cannonicalTimeRange = calculators[CalculatorsType.RegularTime].Calculate();
        return MapResultsToDailyRecord(payload, cannonicalTimeRange);
    }

    private Form48ResultModel MapResultsToDailyRecord(DTRProcessorPayload payload, TimeRange cannonicalTimeRange)
    {
        var context = new TimeContext
        {
            Payload = payload,
            CanonicalTimeRange = cannonicalTimeRange
        };
        var workType = WorkTypeResolver.Resolve(context);
        return Form48Builder.Build(context, workType);
    }
}
public class Form48Builder
{
    public static Form48ResultModel Build(TimeContext context, WorkType workType)
    {
        var data = context.Payload.Data;
        var shift = context.Payload.Data.CurrentShift;
        var emp = context.Payload.Data.Employee;
        var attendances = context.Payload.Data.CurrentAttendance;
        var am = AttendanceHelper.GetPunchNear(shift.StartTime, attendances, 150);
        var pm = AttendanceHelper.GetPunchNear(shift.EndTime, attendances, 150);

        Attendance? lunchOut = null;
        Attendance? lunchIn = null;

        if (shift.LunchBreakOption == BreakMode.UNPAID_BREAK)
        {
            lunchOut = AttendanceHelper.GetPunchNear(shift.LunchStartTime!.Value, attendances, 30);
            lunchIn = AttendanceHelper.GetPunchNear(shift.LunchEndTime!.Value, attendances, 30);
        }

        var pipelineResult = new PipeLineResult
        {
            //OT = new OverTimePipeline(context).Apply(cannonicalTimeRange),
            //Late = new LateTimePipeline(context).Apply(cannonicalTimeRange),
            UT = new UndertimeTimePipeline(context).Apply(context.CanonicalTimeRange),
            //Overbreak = new OverbreaktimePipeline(context).Apply(cannonicalTimeRange),
            //Leave = TimeRange.Empty
        };

        var dtr = new Form48ResultModel
        {
            BioId = emp?.BioId ?? 0,
            FullName =  emp.FullName,
            EmployeeId = emp?.Id ?? Guid.Empty,
            empCode = emp?.BioId.ToString() ?? "",
            WorkDate = data.CurrentDate,
            ShiftName = shift.ShiftName,
            ShiftStartTime = shift.StartTime,
            ShiftEndTime = shift.EndTime,
            WorkType = StringHelpers.AddSpacesBeforeCaps(workType.ToString()).Trim(),
            StartTime = am?.WorkDateTime,
            EndTime = pm?.WorkDateTime,
            BreakOut = lunchOut?.WorkDateTime,
            BreakIn = lunchIn?.WorkDateTime,
            UTHrs = Math.Floor(pipelineResult.UT.ToHour()),
            UTMinutes = Math.Floor(pipelineResult.UT.TotalMinutes % 60)
        };
        return dtr;
    }
}
public class Form48ResultModel
{
    public string SalaryType { get; set; } = string.Empty;
    public string WorkType { get; set; } = "RWD";
    public string FullName { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; } = Guid.Empty;
    public string empCode { get; set; } = string.Empty;
    public int BioId { get; set; } = 0;
    public DateOnly WorkDate { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public DateTime ShiftStartTime { get; set; }
    public DateTime ShiftEndTime { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? BreakOut { get; set; }
    public DateTime? BreakIn { get; set; }
    public double UTHrs { get; set; }
    public double UTMinutes { get; set; }
    public string Remarks { get; set; } = string.Empty;
}