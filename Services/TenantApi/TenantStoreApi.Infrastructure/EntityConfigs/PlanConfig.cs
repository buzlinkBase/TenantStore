using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Infrastructure.EntityConfigs;

/// <summary>
/// The three plans and what each unlocks. These are only the starting values -- prices and
/// entitlements are edited through the platform billing API afterwards, and a change here only
/// reaches the database through a migration.
/// </summary>
public static class PlanSeed
{
    public static readonly Guid LegacyFreeTrialId = Guid.Parse("D8CB5F8B-8E4E-4352-B41C-C311F73B5ED5");
    public static readonly Guid TimekeepingId = Guid.Parse("4E0B7A52-7C1D-4F3A-9C61-1A2B3C4D5E01");
    public static readonly Guid PayrollId = Guid.Parse("4E0B7A52-7C1D-4F3A-9C61-1A2B3C4D5E02");
    public static readonly Guid PremiumId = Guid.Parse("4E0B7A52-7C1D-4F3A-9C61-1A2B3C4D5E03");

    public const string TimekeepingCode = "timekeeping";
    public const string PayrollCode = "payroll";
    public const string PremiumCode = "premium";

    private const string On = "true";
    private const string Off = "false";

    // Placeholder prices (PHP, VAT-inclusive) -- set the real ones through the platform API.
    // Annual = 10 x monthly: two months free.
    private static Plan NewPlan(Guid id, string code, string name, string description, int order, decimal monthly, bool recommended) => new()
    {
        Id = id,
        Code = code,
        Name = name,
        Description = description,
        SortOrder = order,
        IsRecommended = recommended,
        IsPublic = true,
        MonthlyPrice = monthly,
        AnnualPrice = monthly * 10,
        Currency = "PHP",
        PricesIncludeVat = true,
    };

    public static readonly Plan[] Plans =
    [
        new()
        {
            Id = LegacyFreeTrialId,
            Code = "legacy-free-trial",
            Name = "Free Trial",
            Description = "Free Trial",
            Days = 15,
            IsPublic = false,
        },
        NewPlan(TimekeepingId, TimekeepingCode, "Timekeeping",
            "Biometrics, attendance, schedules, DTR and leave/overtime applications.", 1, 1999m, recommended: false),
        NewPlan(PayrollId, PayrollCode, "Payroll",
            "Everything in Timekeeping plus payroll, payslips, loans and government reports.", 2, 4999m, recommended: true),
        NewPlan(PremiumId, PremiumCode, "Premium",
            "Everything in Payroll plus multiple companies, analytics, multi-level approvals and the audit trail.", 3, 9999m, recommended: false),
    ];

    // Key -> (Timekeeping, Payroll, Premium).
    private static readonly (string Key, string Timekeeping, string Payroll, string Premium)[] Matrix =
    [
        (EntitlementKeys.TimekeepingCore, On, On, On),
        (EntitlementKeys.TimekeepingBiometric, On, On, On),
        (EntitlementKeys.ApplicationsCore, On, On, On),
        (EntitlementKeys.PortalSelfService, On, On, On),
        (EntitlementKeys.PayrollCore, Off, On, On),
        (EntitlementKeys.PayrollLoansIncome, Off, On, On),
        (EntitlementKeys.ReportsStatutory, Off, On, On),
        (EntitlementKeys.ApprovalsMultilevel, Off, Off, On),
        (EntitlementKeys.Analytics, Off, Off, On),
        (EntitlementKeys.Announcements, Off, Off, On),
        (EntitlementKeys.AuditTrail, Off, Off, On),
        (EntitlementKeys.ClientsAgency, Off, Off, On),
        (EntitlementKeys.CompaniesMax, "1", "1", "5"),
        (EntitlementKeys.EmployeesMax, "100", "2000", "-1"),
        (EntitlementKeys.AdminsMax, "2", "5", "-1"),
        (EntitlementKeys.BiometricDevicesMax, "1", "3", "-1"),
        (EntitlementKeys.SupportTier, "email", "email", "priority"),
    ];

    public static IEnumerable<PlanEntitlement> Entitlements() =>
        Matrix.SelectMany(row => new[]
        {
            Entitlement(TimekeepingId, row.Key, row.Timekeeping),
            Entitlement(PayrollId, row.Key, row.Payroll),
            Entitlement(PremiumId, row.Key, row.Premium),
        });

    private static PlanEntitlement Entitlement(Guid planId, string key, string value) => new()
    {
        Id = StableId($"{planId}:{key}"),
        PlanId = planId,
        Key = key,
        Value = value,
    };

    // Seed rows need the same Id on every build, or each migration would delete and re-insert them.
    private static Guid StableId(string seed) => new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
}

public class PlanConfig : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
        builder.Property(x => x.AnnualPrice).HasPrecision(18, 2);
        builder.HasData(PlanSeed.Plans);
    }
}

public class PlanEntitlementConfig : IEntityTypeConfiguration<PlanEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanEntitlement> builder)
    {
        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.Value).HasMaxLength(64);
        builder.HasOne(x => x.Plan)
            .WithMany(x => x.Entitlements)
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.PlanId, x.Key }).IsUnique();
        builder.HasData(PlanSeed.Entitlements());
    }
}
