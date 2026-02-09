namespace DTR.Core;

public class CleanDTRDetailProcessor : IDTRProcessor<DailyRecord>
{
    private static Dictionary<CalculatorsType, BaseTimeCalculator> CreateCalculators(DTRProcessorPayload payload)
    {
        return new Dictionary<CalculatorsType, BaseTimeCalculator>
        {
            { CalculatorsType.RegularTime, RegularTimeCalculatorFactory.Create(payload) },
        };
    }

    public DailyRecord? Process(DTRProcessorPayload payload)
    {
        var calculators = CreateCalculators(payload);
        var results = new Dictionary<CalculatorsType, TimeRange>();
        foreach (var key in calculators.Keys)
        {
            if (calculators.TryGetValue(key, out var calculator) && calculator != null)
            {
                try
                {
                    var result = calculator.Calculate();
                    if (result != null)
                    {
                        results[key] = result;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Calculator for {key} failed: {ex.Message}");
                }
            }
        }
        return MapResultsToDailyRecord(payload, results);
    }

    private DailyRecord MapResultsToDailyRecord(DTRProcessorPayload payload, Dictionary<CalculatorsType, TimeRange> results)
    {
        var cannonicalTimeRange = results[CalculatorsType.RegularTime];
        var context = new TimeContext
        {
            Payload = payload,
            CanonicalTimeRange = cannonicalTimeRange
        };

        var pipelineResult = new PipeLineResult
        {
            Regular = new NonHolidayDutyTimePipeline(context).Apply(cannonicalTimeRange),
            LegalHoliday = new HolidayDutyTimePipeline(context, HolidayType.LEGAL).Apply(cannonicalTimeRange),
            SpecialHoliday = new HolidayDutyTimePipeline(context, HolidayType.SPECIAL).Apply(cannonicalTimeRange),
            Plus8 = new HolidayPlus8TimePipeline(context).Apply(cannonicalTimeRange),
            OT = new OverTimePipeline(context).Apply(cannonicalTimeRange),
            Late = new LateTimePipeline(context).Apply(cannonicalTimeRange),
            UT = new UndertimeTimePipeline(context).Apply(cannonicalTimeRange),
            Overbreak = new OverbreaktimePipeline(context).Apply(cannonicalTimeRange),
            Leave = TimeRange.Empty
        };
        var displayContext = new DisplayContext
        {
            TimeContext = context,
            PipeLineResult = pipelineResult
        };

        var evaluated = DTRDetailColumnDisplayProcessor.DisplayRule(displayContext);
        var nightDiffResult = DTRDetailColumnDisplayProcessor.ComputeNightDiff(evaluated, displayContext);
        var workType = WorkTypeResolver.Resolve(context);

        return DailyRecordBuilder.Build(context, pipelineResult,
            evaluated,
            nightDiffResult,
            workType);
    }
}
public class DailyRecordBuilder
{
    public static DailyRecord Build(
        TimeContext context,
        PipeLineResult pipeline,
        EvaluatedColumnResult evaluated,
        NightDiffEvaluationResult NightDiff,
        WorkType workType
        )
    {
        var emp = context.Payload.Data.Employee;
        int recordState = (int)DTRStatus.OPEN;
        if (context.Payload.Data.CurrentAttendance.Any()
            && context.CanonicalTimeRange.TimeRecords.MinBy(x => x.StartTime)?.StartTime != null)
        {
            recordState = (int)context.Payload.Data.CurrentAttendance[0].RecordStatus;
        }
        var state = (DTRStatus)recordState;

        var dtr = new DailyRecord
        {
            ShiftWorkingHour = context.Payload.Data.CurrentShift.MaxWorkingMinutes / 60,
            AttStatus = recordState,
            RecordStatus = state,
            HolCount = pipeline.Plus8.GetMetaData<int>("HolidayCount"),
            SPCount = pipeline.Plus8.GetMetaData<int>("SPHolidayCount"),
            FullName = emp.FullName,
            EmployeeId = emp.Id,
            BioId = emp?.BioId ?? 0,
            empCode = emp?.BioId.ToString() ?? "",
            WorkDate = context.Payload.Data.CurrentDate,
            ShiftName = context.Payload.Data.CurrentShift.ShiftName,
            ShiftStartTime = context.Payload.Data.CurrentShift.StartTime,
            ShiftEndTime = context.Payload.Data.CurrentShift.EndTime,
            StartTime = context.CanonicalTimeRange.TimeRecords.MinBy(x => x.StartTime)?.StartTime,
            EndTime = context.CanonicalTimeRange.TimeRecords.MaxBy(x => x.EndTime)?.EndTime,
            WorkType = StringHelpers.AddSpacesBeforeCaps(workType.ToString()).Trim(),
            WorkTypeEnum= workType,
            //hol Day
            LHHolidayTotalDays = evaluated.LegalHoliday.TotalMinutes.ToDays(),
            SPHolidayTotalDays = evaluated.SPHoliday.TotalMinutes.ToDays(),
            //Regular Working Days
            RegularWorkingDays = evaluated.RegWork.TotalMinutes.ToDays(),
            RegularNDDays = NightDiff.Regular.TotalMinutes.ToDays(),
            RegularOTDays = evaluated.RegOT.TotalMinutes.ToDays(),
            RegularNDOTDays = NightDiff.RegOT.TotalMinutes.ToDays(),


            //rest Working Days
            RestDayDays = evaluated.RestWork.ToDays(),
            RestDayNDDays = NightDiff.Rest.TotalMinutes.ToDays(),
            RestDayOTDays = evaluated.RestOT.ToDays(),
            RestDayNDODays = NightDiff.RestOT.TotalMinutes.ToDays(),

            //minutes
            LateMinutes = pipeline.Late.TotalMinutes,
            UTMinutes = pipeline.UT.TotalMinutes,
            OverBreakMinutes = pipeline.Overbreak.TotalMinutes,
            OTMinutes = pipeline.OT.TotalMinutes,
            ND = NightDiff.ND.TotalMinutes,
            NDOT = NightDiff.NDOT.TotalMinutes,
            LH = evaluated.LegalHoliday.TotalMinutes,
            SP = pipeline.SpecialHoliday.TotalMinutes,

            LegalHolOTMinutes = evaluated.LHOT.TotalMinutes,
            LegalHolNightDiffMinutes = NightDiff.LH.TotalMinutes,
            LegalHolNightDiffOTMinutes = NightDiff.LHOT.TotalMinutes,

            SpecialHolOTMinutes = evaluated.SPOT.TotalMinutes,
            SpecialHolNightDiffMinutes = NightDiff.SP.TotalMinutes,
            SpecialHolNightDiffOTMinutes = NightDiff.SPOT.TotalMinutes,

            //reg
            RegDayMinutes = evaluated.RegWork.TotalMinutes,
            RegDayNDMinutes = NightDiff.Regular.TotalMinutes,
            RegDayOTMinutes = evaluated.RegOT.TotalMinutes,
            RegDayNDOMinutes = NightDiff.RegOT.TotalMinutes,

            //rest
            RestDayMinutes = evaluated.RestWork.TotalMinutes,
            RestDayNDMinutes = NightDiff.Rest.TotalMinutes,
            RestDayOTMinutes = evaluated.RestOT.TotalMinutes,
            RestDayNDOMinutes = NightDiff.RestOT.TotalMinutes,
            LeaveMinutes = pipeline.Leave.TotalMinutes,

            //hours
            LateHours = pipeline.Late.TotalMinutes.ToHour(),
            OverBreakHours = pipeline.Overbreak.TotalMinutes.ToHour(),
            LateForOTHours = 0,

            RegularNetHours = evaluated.RegWork.TotalMinutes.ToHour(),
            RegularOTHours = evaluated.RegOT.TotalMinutes.ToHour(),
            RegularNDHours = NightDiff.Regular.TotalMinutes.ToHour(),
            RegularNDOTHours = NightDiff.RegOT.TotalMinutes.ToHour(),

            RestDayHours = evaluated.RestWork.TotalMinutes.ToHour(),
            RestDayOTHours = evaluated.RestOT.TotalMinutes.ToHour(),
            RestDayNDHours = NightDiff.Rest.TotalMinutes.ToHour(),
            RestDayNDOTHours = NightDiff.RestOT.TotalMinutes.ToHour(),

            LegalHolHours = evaluated.LegalHoliday.TotalMinutes.ToHour(),
            LegalHolOTHours = evaluated.LHOT.TotalMinutes.ToHour(),
            LegalHolNightDiffHours = NightDiff.LH.TotalMinutes.ToHour(),
            LegalHolNightDiffOTHours = NightDiff.LHOT.TotalMinutes.ToHour(),

            SpecialHolHours = evaluated.SPHoliday.TotalMinutes.ToHour(),
            SpecialHolOTHours = evaluated.SPOT.TotalMinutes.ToHour(),
            SpecialHolNightDiffHours = NightDiff.SP.TotalMinutes.ToHour(),
            SpecialHolNightDiffOTHours = NightDiff.SPOT.TotalMinutes.ToHour(),

            RestLegalDayHours = (evaluated.RestWork + evaluated.LegalHoliday).TotalMinutes.ToHour(),
            RestLegalDayOTHours = (evaluated.RestOT + evaluated.LHOT).TotalMinutes.ToHour(),
            RestLegalDayNDHours = (NightDiff.Rest + NightDiff.LH).TotalMinutes.ToHour(),
            RestLegalDayNDOTHours = (NightDiff.RestOT + NightDiff.LHOT).TotalMinutes.ToHour(),

            RestSpecialDayHours = (evaluated.RestWork + evaluated.SPHoliday).TotalMinutes.ToHour(),
            RestSpecialDayOTHours = (NightDiff.Rest + NightDiff.SP).TotalMinutes.ToHour(),
            RestSpecialDayNDHours = (evaluated.RestOT + evaluated.SPOT).TotalMinutes.ToHour(),
            RestSpecialDayNDOTHours = (NightDiff.RestOT + NightDiff.SPOT).TotalMinutes.ToHour(),

            OB = 0,
            Absent = workType == WorkType.Absent ? 1 : 0,
            DepartmentId = emp?.DepartmentId,
            PayrollGroupId = emp?.PayrollGroupId,
            ClientId = emp?.ClientId,
        };
        return dtr;
    }
}