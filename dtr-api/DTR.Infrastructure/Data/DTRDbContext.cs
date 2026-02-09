
namespace DTR.Infrastructure;
public class DTRDbContext : DbContext
{
    public readonly Guid TenantId = Guid.Empty;
    public DTRDbContext(DbContextOptions options, Guid tenantId) : base(options)
    {
        TenantId = tenantId;
    }
    public DTRDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.EnableSensitiveDataLogging();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        if (TenantId != Guid.Empty)
        {
            modelBuilder.UseSoftDelete(TenantId);
        }
    }

    //public DbSet<Employee> Employees { get; set; }
    public DbSet<DailyRecord> DailyRecords { get; set; }
    public DbSet<Attendance> Attendances { get; set; }
    public DbSet<ManualBatchEntryLogEntity> ManualBatchEntryLogs { get; set; }
    public DbSet<UnkownEmpAttendance> UnknownAtt { get; set; }
    //public DbSet<TimeShift> TimeShifts { get; set; }
    //public DbSet<WorkSchedulePlan> WorkSchedulePlans { get; set; }
    //public DbSet<Department> Departments { get; set; }
    //public DbSet<OperationArea> OperationAreas { get; set; }
    //public DbSet<PayrollGroup> PayrollGroup { get; set; }
    //public DbSet<Client> Clients { get; set; }
    //public DbSet<ClientHoliday> ClientHolidays { get; set; }
    //public DbSet<ChangeHoliday> ChangeHolidays { get; set; }
    //public DbSet<RestDay> DayOffs { get; set; }
    //public DbSet<RestDayDate>  RestDayDates { get; set; }
    //public DbSet<ChangeRestDay> ChangeRestDays { get; set; }
    //public DbSet<Holiday> Holidays { get; set; }
    //public DbSet<LogLimit> LogLimits { get; set; }
    //public DbSet<LeaveEntity> Leaves { get; set; }
    //public DbSet<LeaveApplication> LeaveApplications { get; set; }
    //public DbSet<LeaveApplicationDetail> LeaveApplicationDetails { get; set; }
    //public DbSet<OverTimeApplicationEntity> OverTimeApplications { get; set; }
    //public DbSet<UnderTimeApplication> UnderTimeApplications { get; set; }
    //public DbSet<DailyRecordPunchCounter> DTRPunchCounters { get; set; }
    //public DbSet<GeneralSetting> GeneralSettings { get; set; }
    //public DbSet<OutboxMessage> OutboxMessages { get; set; }
}

public class AppDbContextFactory : IDesignTimeDbContextFactory<DTRDbContext>
{
    public DTRDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DTRDbContext>();
        var constr = "server=127.0.0.1;port=3316;database=dtrapi;user=oneuser;password=Pokemon67584321";
        optionsBuilder.UseMySql(constr, ServerVersion.AutoDetect(constr));
        return new DTRDbContext(optionsBuilder.Options);
    }
}