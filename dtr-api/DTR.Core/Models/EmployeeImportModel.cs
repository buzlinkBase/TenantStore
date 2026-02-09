namespace DTR.Core;

public class EmployeeImportModel
{
    public int BioId { get; set; }
    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public string Suffix { get; set; }
    public string Gender { get; set; }
    public string RestDay1 { get; set; }
    public string RestDay2 { get; set; }

    public string DepartmentName { get; set; }
    public string ClientName { get; set; }
    public string PayrollGroup { get; set; }
    public string ShiftName { get; set; }
    public string ShiftType { get; set; }
    //public string CrossDate { get; set; }
    public string AMIn { get; set; }
    public string NoonBreakOut { get; set; }
    public string NoonBreakIn { get; set; }
    public string PMOut { get; set; }

}
