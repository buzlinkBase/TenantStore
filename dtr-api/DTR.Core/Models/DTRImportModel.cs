namespace DTR.Core;

public class DTRImportModel
{
    public int BioId { get; set; }
    public string Employee { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public double Late_Minutes { get; set; }
    public double UT_Minutes { get; set; }
    public double Over_Minutes { get; set; }
    public double OT_Minutes { get; set; }
    public double ND_Minutes { get; set; }
    public double NDOT_Minutes { get; set; }
    public double LH_Minutes { get; set; }
    public double SP_Minutes { get; set; }
    public double Reg_Days { get; set; }
    public double RND_Days { get; set; }
    public double ROT_Days { get; set; }
    public double RNDO_Days { get; set; }
    public double RestDay { get; set; }
    public double RDND_Days { get; set; }
    public double RDOT_Days { get; set; }
    public double RDNDO_Days { get; set; }
    public double LH_Days { get; set; }
    public double SP_Days { get; set; }
    public double RegNet_Hours { get; set; }
    public double NetOT_Hours { get; set; }
    public double ND_Hours { get; set; }
    public double NDOT_Hours { get; set; }
    public double RDNet_Hours { get; set; }
    public double RDOT_Hours { get; set; }
    public double RDND_Hours { get; set; }
    public double RDNDOT_Hours { get; set; }
    public double LH_Hours { get; set; }
    public double LHOT_Hours { get; set; }
    public double LHND_Hours { get; set; } 
    public double LHNDOT_Hours { get; set; }
    public double SPH_Hours { get; set; }
    public double SPHOT_Hours { get; set; }
    public double SPHND_Hours { get; set; }
    public double SPHNDOT_Hours { get; set; }
    public int Absent_Hours { get; set; }
    public double Total { get; set; }
}