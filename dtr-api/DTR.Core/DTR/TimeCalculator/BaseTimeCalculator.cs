namespace DTR.Core;

public abstract class BaseTimeCalculator
{
    protected readonly DTRProcessorPayload _payload;
    private BaseTimeCalculator _nextHandler;
    public DTRProcessorPayload Payload => _payload;
    protected BaseTimeCalculator(DTRProcessorPayload payload)
    {
        _payload = payload;
    }
    protected virtual bool IsValid()
    {
        if (_payload.Data.Employee == null
            || _payload.Data.CurrentShift == null
            || !_payload.Data.CurrentAttendance.Any()
            || _payload.Data.CurrentAttendance.Count() < 2 
            )
        {
            return false;
        }

        var endAtt = _payload.Data.CurrentAttendance.LastOrDefault();
        var startAtt = _payload.Data.CurrentAttendance.FirstOrDefault();
        if (startAtt == null || endAtt == null || startAtt.WorkDateTime == endAtt.WorkDateTime)
        {
            return false;
        } 
        return true;
    }

    protected abstract TimeRange Processor();
    public TimeRange Calculate()
    {
        if (IsValid())
        {
            return Processor();
        }
        if (_nextHandler != null)
        {
            return _nextHandler.Calculate();
        }
        return new TimeRange();
    }
    public void SetNext(BaseTimeCalculator nextHandler)
    {
        _nextHandler = nextHandler;
    }
}
