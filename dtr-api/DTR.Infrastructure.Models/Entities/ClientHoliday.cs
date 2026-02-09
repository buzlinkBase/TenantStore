namespace DTR.Models;

public class ClientHoliday : BaseEntity
{
    public Guid ClientId { get; set; }
    public virtual Client Client { get; set; }
    public string Description { get; set; } = string.Empty;
    public HolidayType HolType { get; set; } = HolidayType.LEGAL;
    public HolidayWorkType WorkType { get; set; } = HolidayWorkType.NonWorking;
    public int HolYear { get; set; }
    public DateOnly HolDate { get; set; }
    public bool IsRecuring { get; set; }
    public bool IsPaid { get; set; }
}