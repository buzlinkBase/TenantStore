namespace DTR.Core;

public class RegularTimeCalculatorFactory
{
    public static BaseTimeCalculator Create(DTRProcessorPayload payload)
    {
        var current = new RegularCurrentScheduledTimeCalculator(payload);
        return current;
    }
}