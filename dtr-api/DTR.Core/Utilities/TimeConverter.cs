namespace DTR.Core;

public class TimeConverter
{
    public static double MinutesToHour(double minutes)
    {
        return minutes / 60;
    }
    public static double HourToMinutes(double hours)
    {
        return hours * 60;
    }
    public static double MinutesToDays(double minutes)
    {
        return minutes / (24 * 60);
    }
}
